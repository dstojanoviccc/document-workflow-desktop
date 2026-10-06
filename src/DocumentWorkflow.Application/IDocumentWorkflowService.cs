namespace DocumentWorkflow.Application;

public interface IDocumentWorkflowService
{
    Task<IReadOnlyList<DocumentSnapshot>> ListAsync(CancellationToken cancellationToken = default);
    Task CheckOutAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<string?> CheckInAsync(Guid documentId, CancellationToken cancellationToken = default);
    async Task<CheckInResult> CheckInWithResultAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        new(true, Message: await CheckInAsync(documentId, cancellationToken));
    Task OpenAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<string?> DiscardAsync(Guid documentId, CancellationToken cancellationToken = default, bool allowModified = false);
    Task ReconcileAsync(CancellationToken cancellationToken = default);
    Task SaveLocalCopyAsync(Guid documentId, string destination, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task OpenLatestAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task<DocumentHistoryDetails> GetHistoryAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task OpenVersionAsync(Guid documentId, int versionNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
public interface IWorkingCopyOpener
{
    void Open(string path);
}
