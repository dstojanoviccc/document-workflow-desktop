using DocumentWorkflow.Domain;
using Microsoft.Extensions.Logging;

namespace DocumentWorkflow.Application;

public sealed class DocumentWorkflowService(IWorkflowStore store, IDocumentSource source, IWorkspaceService workspace,
    IFileHashService hashes, ILogger<DocumentWorkflowService> logger, IWorkingCopyOpener opener, WorkingCopyStateService? evaluator = null,
    IVersionContentStore? versions = null) : IDocumentWorkflowService
{
    private readonly WorkingCopyStateService states = evaluator ?? new(workspace, hashes, logger);
    public async Task<IReadOnlyList<DocumentSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        var documents = await store.ListAsync(cancellationToken).ConfigureAwait(false);
        var evaluated = new List<DocumentSnapshot>(documents.Count);
        foreach (var item in documents)
        {
            if (item.WorkingCopy is null) { evaluated.Add(item); continue; }
            var result = await states.EvaluateAsync(item.Document, item.WorkingCopy, cancellationToken).ConfigureAwait(false);
            evaluated.Add(item with { Evaluation = result, WorkspaceWarning = result.Warning });
        }
        return evaluated;
    }
    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var documents = await ListAsync(cancellationToken);
        logger.LogInformation("Startup reconciliation inspected {WorkingCopyCount} working copies; {WarningCount} need attention",
            documents.Count(x => x.WorkingCopy is not null), documents.Count(x => x.WorkspaceWarning is not null));
    }
    public async Task CheckOutAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Checkout started for {DocumentId}", documentId);
        string? createdPath = null;
        DocumentRecord? document = null;
        var committed = false;
        try
        {
            await using var session = await store.BeginAsync(documentId, cancellationToken);
            if (session.WorkingCopy is not null || session.Document.Status != WorkingCopyState.Available)
                throw new WorkflowException("This document is already checked out. Open or discard its existing local copy.");
            document = session.Document;
            var currentPath = document.CurrentVersion == 1 ? source.Resolve(document.FileName)
                : (versions ?? throw new WorkflowException("Version storage is not configured.")).Resolve(document.Id, document.CurrentVersion, document.FileName);
            createdPath = await workspace.CopyAsync(document.Id, document.FileName, currentPath, cancellationToken);
            var hash = await hashes.HashAsync(createdPath, cancellationToken);
            session.Add(new WorkingCopy(document.Id, createdPath, document.CurrentVersion, hash));
            document.CheckOut();
            await session.CommitAsync(cancellationToken);
            committed = true;
            logger.LogInformation("Checkout completed for {DocumentId} at base version {BaseVersion}", document.Id, document.CurrentVersion);
        }
        catch (Exception error)
        {
            logger.LogError(error, "Checkout failed for {DocumentId}", documentId);
            if (createdPath is not null && !committed)
            {
                try { workspace.Delete(documentId, document!.FileName, createdPath); }
                catch (Exception cleanupError) { logger.LogError(cleanupError, "Checkout file cleanup failed for {DocumentId}", documentId); }
            }
            if (error is WorkflowException) throw;
            throw new WorkflowException("Checkout failed. Ensure the demo source exists and the workspace is writable; then try again. Existing local files are never overwritten.", error);
        }
    }
    public async Task OpenAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var session = await store.BeginAsync(documentId, cancellationToken);
            var copy = session.WorkingCopy ?? throw new WorkflowException("Check out this document before opening a local file.");
            var evaluation = await states.EvaluateAsync(session.Document, copy, cancellationToken);
            if (evaluation.Issue != EvaluationIssue.None)
                throw new WorkflowException(evaluation.Warning ?? "The local file could not be evaluated. Refresh and try again.");
            opener.Open(copy.LocalPath);
            logger.LogInformation("Opened working copy for {DocumentId}", documentId);
        }
        catch (Exception error)
        {
            logger.LogError(error, "Open working copy failed for {DocumentId}", documentId);
            if (error is WorkflowException) throw;
            throw new WorkflowException("The local file could not be opened. Check its permissions and install or choose a default application for this file type in Windows.", error);
        }
    }
    public Task<string?> CheckInAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        Task.Run(() => CheckInCoreAsync(documentId, cancellationToken), cancellationToken);

    private async Task<string?> CheckInCoreAsync(Guid documentId, CancellationToken cancellationToken)
    {
        logger.LogInformation("Check-in started for {DocumentId}", documentId);
        var content = versions ?? throw new WorkflowException("Version storage is not configured.");
        DocumentRecord? document = null;
        var next = 0;
        var prepared = false;
        var committed = false;
        try
        {
            await using var session = await store.BeginAsync(documentId, cancellationToken);
            document = session.Document;
            var copy = session.WorkingCopy ?? throw new WorkflowException("Check out and modify this document before checking it in.");
            var evaluation = await states.EvaluateAsync(document, copy, cancellationToken);
            if (evaluation.Issue != EvaluationIssue.None)
                throw new WorkflowException(evaluation.Warning ?? "The working file cannot be read. Your checkout has been kept.");
            if (evaluation.State != WorkingCopyState.Modified)
                throw new WorkflowException("The working copy is unchanged. No new version was created.");
            if (copy.BaseVersion != document.CurrentVersion)
                throw new WorkflowException("The checkout base no longer matches the current version. Your edits have been kept; conflict resolution is not available yet.");
            next = checked(document.CurrentVersion + 1);
            var actual = await content.CreateAsync(document.Id, next, document.FileName, copy.LocalPath, evaluation.CurrentHash!, cancellationToken);
            prepared = true;
            // Restore this file if metadata commit fails. Verify it again after rename so an edit
            // between artifact preparation and staging cannot be silently lost.
            using var deletion = workspace.StageDeletion(document.Id, document.FileName, copy.LocalPath);
            await using (var locked = new FileStream(deletion.StagedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            {
                if (!string.Equals(await hashes.HashAsync(deletion.StagedPath, cancellationToken), actual, StringComparison.OrdinalIgnoreCase))
                    throw new WorkflowException("The working file changed during check-in. Your edits have been kept. Close the editor, Refresh and retry.");
                var version = new DocumentVersion(document.Id, next, actual, "Local check-in.", copy.BaseVersion);
                session.AddVersion(version);
                document.CompleteCheckIn(copy.BaseVersion, next, version.CreatedAt);
                session.RemoveWorkingCopy();
                await session.CommitAsync(cancellationToken);
                committed = true;
            }
            states.Forget(documentId);
            logger.LogInformation("Check-in completed for {DocumentId}: version {VersionNumber}, hash {FileHash}", documentId, next, actual);
            try { deletion.Complete(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(error, "Check-in committed but local staged file cleanup failed for {DocumentId}", documentId);
                return "Check-in succeeded, but a local .discard file remains. Close the editor and remove that leftover file. The new version is safely stored.";
            }
            return null;
        }
        catch (Exception error)
        {
            if (prepared && !committed)
            {
                try { content.Delete(documentId, next, document!.FileName); }
                catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
                { logger.LogError(cleanupError, "Unpublished version cleanup failed for {DocumentId}; retry will not overwrite it", documentId); }
            }
            logger.LogError(error, "Check-in failed for {DocumentId}", documentId);
            if (error is WorkflowException or OperationCanceledException) throw;
            throw new WorkflowException("Check-in failed. Your checkout and local edits have been retained. Close the editor and check version-storage permissions before retrying.", error);
        }
    }
    public async Task<string?> DiscardAsync(Guid documentId, CancellationToken cancellationToken = default, bool allowModified = false)
    {
        logger.LogInformation("Discard started for {DocumentId}", documentId);
        try
        {
            await using var session = await store.BeginAsync(documentId, cancellationToken);
            var copy = session.WorkingCopy ?? throw new WorkflowException("This document has no active checkout to discard.");
            var evaluation = await states.EvaluateAsync(session.Document, copy, cancellationToken);
            if (!allowModified && (evaluation.State == WorkingCopyState.Modified || evaluation.Issue == EvaluationIssue.Unreadable))
                throw new WorkflowException("The local copy contains edits or could not be verified. Review the modified-file warning and explicitly confirm discard before continuing.");
            using var deletion = workspace.StageDeletion(documentId, session.Document.FileName, copy.LocalPath);
            session.RemoveWorkingCopy();
            session.Document.DiscardCheckout();
            await session.CommitAsync(cancellationToken);
            try { deletion.Complete(); }
            catch (Exception cleanupError)
            {
                logger.LogError(cleanupError, "Discard committed but staged file cleanup failed for {DocumentId}", documentId);
                return "Checkout discarded, but its staged .discard file could not be deleted. Close the external editor and remove that leftover file from the document workspace directory.";
            }
            states.Forget(documentId);
            logger.LogInformation("Discard completed for {DocumentId}", documentId);
            return null;
        }
        catch (Exception error)
        {
            logger.LogError(error, "Discard failed for {DocumentId}", documentId);
            if (error is WorkflowException) throw;
            throw new WorkflowException("Discard failed. Close the file in its external application and check workspace permissions before trying again. Checkout metadata has been retained.", error);
        }
    }
}
