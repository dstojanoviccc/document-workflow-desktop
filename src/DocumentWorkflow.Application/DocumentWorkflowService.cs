using DocumentWorkflow.Domain;
using Microsoft.Extensions.Logging;

namespace DocumentWorkflow.Application;

public sealed class DocumentWorkflowService(IWorkflowStore store, IDocumentSource source, IWorkspaceService workspace,
    IFileHashService hashes, ILogger<DocumentWorkflowService> logger, IWorkingCopyOpener opener, WorkingCopyStateService? evaluator = null) : IDocumentWorkflowService
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
            createdPath = await workspace.CopyAsync(document.Id, document.FileName, source.Resolve(document.FileName), cancellationToken);
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
