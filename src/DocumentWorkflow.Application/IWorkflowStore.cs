using DocumentWorkflow.Domain;

namespace DocumentWorkflow.Application;

public interface IWorkflowStore
{
    Task<IWorkflowSession> BeginAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentSnapshot>> ListAsync(CancellationToken cancellationToken = default);
    Task<DocumentHistory> GetHistoryAsync(Guid documentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
public interface IWorkflowSession : IAsyncDisposable
{
    DocumentRecord Document { get; }
    WorkingCopy? WorkingCopy { get; }
    IReadOnlyList<DocumentVersion> Versions { get; }
    void Add(WorkingCopy copy);
    void AddVersion(DocumentVersion version);
    Task<bool> AppendEventAsync(WorkflowEvent value, CancellationToken cancellationToken = default);
    void RemoveWorkingCopy();
    Task CommitAsync(CancellationToken cancellationToken = default);
}
public sealed record DocumentSnapshot(DocumentRecord Document, WorkingCopy? WorkingCopy, string? WorkspaceWarning = null,
    WorkingCopyEvaluation? Evaluation = null);
public sealed record DocumentHistory(DocumentSnapshot Context, IReadOnlyList<DocumentVersion> Versions, IReadOnlyList<WorkflowEvent> Events);
public sealed record VersionHistoryItem(DocumentVersion Version, bool IsCurrent, string ArtifactPath);
public sealed record DocumentHistoryDetails(DocumentSnapshot Context, IReadOnlyList<VersionHistoryItem> Versions, IReadOnlyList<WorkflowEvent> Events);
public sealed class WorkflowException(string message, Exception? inner = null) : Exception(message, inner);
