namespace DocumentWorkflow.Application;

public interface IDocumentWorkflowService
{
    Task<IReadOnlyList<DocumentSnapshot>> ListAsync(CancellationToken cancellationToken = default);
    Task CheckOutAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task OpenAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<string?> DiscardAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task ReconcileAsync(CancellationToken cancellationToken = default);
}
public interface IWorkingCopyOpener
{
    void Open(string path);
}
