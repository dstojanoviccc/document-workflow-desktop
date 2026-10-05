using DocumentWorkflow.Domain;
using Microsoft.Extensions.Logging;

namespace DocumentWorkflow.Application;

public sealed class DocumentWorkflowService(IWorkflowStore store, IDocumentSource source, IWorkspaceService workspace,
    IFileHashService hashes, ILogger<DocumentWorkflowService> logger, IWorkingCopyOpener opener) : IDocumentWorkflowService
{
    public async Task<IReadOnlyList<DocumentSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        var documents = await store.ListAsync(cancellationToken);
        return documents.Select(item =>
        {
            if (item.WorkingCopy is null) return item;
            try
            {
                return workspace.Exists(item.Document.Id, item.Document.FileName, item.WorkingCopy.LocalPath)
                    ? item : item with { WorkspaceWarning = "The local file is missing. Open is unavailable. Discard clears this checkout; the source file is unchanged." };
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                logger.LogWarning(error, "Working copy path could not be reconciled for {DocumentId}", item.Document.Id);
                return item with { WorkspaceWarning = "The local workspace cannot be accessed safely. Restore its path or permissions before opening or discarding this checkout." };
            }
        }).ToList();
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
            if (!workspace.Exists(documentId, session.Document.FileName, copy.LocalPath))
                throw new WorkflowException("The local file is missing. Discard the checkout to return to Available; no file is recreated automatically.");
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
    public async Task<string?> DiscardAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Discard started for {DocumentId}", documentId);
        try
        {
            await using var session = await store.BeginAsync(documentId, cancellationToken);
            var copy = session.WorkingCopy ?? throw new WorkflowException("This document has no active checkout to discard.");
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
