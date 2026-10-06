using DocumentWorkflow.Domain;
using Microsoft.Extensions.Logging;

namespace DocumentWorkflow.Application;

public sealed partial class DocumentWorkflowService(IWorkflowStore store, IDocumentSource source, IWorkspaceService workspace,
    IFileHashService hashes, ILogger<DocumentWorkflowService> logger, IWorkingCopyOpener opener, WorkingCopyStateService? evaluator = null,
    IVersionContentStore? versions = null, IWorkflowRecovery? recovery = null) : IDocumentWorkflowService
{
    private readonly WorkingCopyStateService states = evaluator ?? new(workspace, hashes, logger);
    public async Task<IReadOnlyList<DocumentSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        var documents = await store.ListAsync(cancellationToken).ConfigureAwait(false);
        var evaluated = new List<DocumentSnapshot>(documents.Count);
        foreach (var item in documents)
        {
            var current = item;
            string? recoveryWarning = null;
            if (recovery is not null)
            {
                await using var session = await store.BeginAsync(item.Document.Id, cancellationToken);
                recoveryWarning = await recovery.RecoverAsync(session.Document, session.WorkingCopy, session.Versions, cancellationToken);
                current = new(session.Document, session.WorkingCopy);
            }
            if (current.WorkingCopy is null) { evaluated.Add(current with { WorkspaceWarning = recoveryWarning }); continue; }
            var result = await states.EvaluateAsync(current.Document, current.WorkingCopy, cancellationToken).ConfigureAwait(false);
            evaluated.Add(current with { Evaluation = result, WorkspaceWarning = result.Warning ?? recoveryWarning });
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
    public async Task<string?> CheckInAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        (await CheckInWithResultAsync(documentId, cancellationToken)).Message;

    public Task<CheckInResult> CheckInWithResultAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        Task.Run(() => CheckInCoreAsync(documentId, cancellationToken), cancellationToken);

    private async Task<CheckInResult> CheckInCoreAsync(Guid documentId, CancellationToken cancellationToken)
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
            if (copy.BaseVersion != document.CurrentVersion)
            {
                var edits = evaluation.State == WorkingCopyState.Conflict;
                logger.LogWarning("Stale checkout blocked for {DocumentId}: base {BaseVersion}, current {CurrentVersion}", documentId, copy.BaseVersion, document.CurrentVersion);
                return new(false, new(copy.BaseVersion, document.CurrentVersion, copy.LocalPath, edits),
                    $"The document advanced from v{copy.BaseVersion} to v{document.CurrentVersion}. Your local copy has been kept. Inspect latest, save your copy or explicitly discard the stale checkout.");
            }
            if (evaluation.State != WorkingCopyState.Modified)
                throw new WorkflowException("The working copy is unchanged. No new version was created.");
            next = checked(document.CurrentVersion + 1);
            var actual = await content.CreateAsync(document.Id, next, document.FileName, copy.LocalPath, evaluation.CurrentHash!, cancellationToken);
            prepared = true;
            // Restore this file if metadata commit fails. Verify it again after rename so an edit
            // between artifact preparation and staging cannot be silently lost.
            using var deletion = workspace.StageDeletion(document.Id, document.FileName, copy.LocalPath);
            using (var locked = deletion.AcquireReadLock())
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
                return new(true, Message: "Check-in succeeded, but a local .discard file remains. Refresh will attempt conservative recovery. The new version is safely stored.");
            }
            return new(true);
        }
        catch (Exception error)
        {
            if (prepared && !committed)
            {
                try
                {
                    // Commit reporting can fail after durability. The disposed failed session is
                    // no longer authoritative: consult committed metadata before deleting any artifact.
                    await using var verification = await store.BeginAsync(documentId, CancellationToken.None);
                    var published = verification.Versions.SingleOrDefault(x => x.VersionNumber == next);
                    if (published is not null)
                    {
                        logger.LogWarning(error, "Check-in commit confirmed from metadata after completion error for {DocumentId}", documentId);
                        if (verification.WorkingCopy is null && workspace.Exists(documentId, document!.FileName, workspace.Resolve(documentId, document.FileName)))
                        {
                            using var leftover = workspace.StageDeletion(documentId, document.FileName, workspace.Resolve(documentId, document.FileName));
                            bool matches;
                            using (leftover.AcquireReadLock()) matches = string.Equals(await hashes.HashAsync(leftover.StagedPath), published.FileHash, StringComparison.OrdinalIgnoreCase);
                            if (matches) leftover.Complete(); // Only remove bytes already stored in the committed version.
                        }
                        return new(true, Message: "Check-in was committed. Completion reporting was interrupted; Refresh to verify the current version. Any additional local edits have been retained.");
                    }
                    content.Delete(documentId, next, document!.FileName);
                }
                catch (Exception cleanupError)
                {
                    // If metadata cannot be verified, preserve the artifact for conservative reconciliation.
                    logger.LogError(cleanupError, "Check-in recovery deferred for {DocumentId}; no unverified artifact is removed", documentId);
                }
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
            if (!allowModified && (evaluation.State is WorkingCopyState.Modified or WorkingCopyState.Conflict || evaluation.Issue == EvaluationIssue.Unreadable))
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
