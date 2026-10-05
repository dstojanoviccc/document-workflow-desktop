using DocumentWorkflow.Domain;
using Microsoft.Extensions.Logging;

namespace DocumentWorkflow.Application;

public sealed class DocumentWorkflowService(IWorkflowStore store, IDocumentSource source, IWorkspaceService workspace,
    IFileHashService hashes, ILogger<DocumentWorkflowService> logger)
{
    public async Task CheckOutAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Checkout started for {DocumentId}", documentId);
        await using var session = await store.BeginAsync(documentId, cancellationToken);
        string? createdPath = null;
        try
        {
            if (session.WorkingCopy is not null || session.Document.Status != WorkingCopyState.Available)
                throw new WorkflowException("This document is already checked out. Open or discard its existing local copy.");
            var document = session.Document;
            createdPath = await workspace.CopyAsync(document.Id, document.FileName, source.Resolve(document.FileName), cancellationToken);
            var hash = await hashes.HashAsync(createdPath, cancellationToken);
            session.Add(new WorkingCopy(document.Id, createdPath, document.CurrentVersion, hash));
            document.CheckOut();
            await session.CommitAsync(cancellationToken);
            logger.LogInformation("Checkout completed for {DocumentId} at base version {BaseVersion}", document.Id, document.CurrentVersion);
        }
        catch (Exception error)
        {
            logger.LogError(error, "Checkout failed for {DocumentId}", documentId);
            if (createdPath is not null)
            {
                try { workspace.Delete(documentId, session.Document.FileName, createdPath); }
                catch (Exception cleanupError) { logger.LogError(cleanupError, "Checkout file cleanup failed for {DocumentId}", documentId); }
            }
            if (error is WorkflowException) throw;
            throw new WorkflowException("Checkout failed. Ensure the demo source exists and the workspace is writable; then try again. Existing local files are never overwritten.", error);
        }
    }
}
