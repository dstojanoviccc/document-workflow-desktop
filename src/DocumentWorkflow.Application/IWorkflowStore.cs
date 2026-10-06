using DocumentWorkflow.Domain;

namespace DocumentWorkflow.Application;

public interface IWorkflowStore
{
    Task<IWorkflowSession> BeginAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentSnapshot>> ListAsync(CancellationToken cancellationToken = default);
}
public interface IWorkflowSession : IAsyncDisposable
{
    DocumentRecord Document { get; }
    WorkingCopy? WorkingCopy { get; }
    void Add(WorkingCopy copy);
    void AddVersion(DocumentVersion version);
    void RemoveWorkingCopy();
    Task CommitAsync(CancellationToken cancellationToken = default);
}
public sealed record DocumentSnapshot(DocumentRecord Document, WorkingCopy? WorkingCopy, string? WorkspaceWarning = null,
    WorkingCopyEvaluation? Evaluation = null);
public sealed class WorkflowException(string message, Exception? inner = null) : Exception(message, inner);
