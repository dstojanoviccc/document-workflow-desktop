using DocumentWorkflow.Domain;

namespace DocumentWorkflow.Application;

public interface IWorkflowRecovery
{
    Task<string?> RecoverAsync(DocumentRecord document, WorkingCopy? copy, IReadOnlyList<DocumentVersion> versions,
        CancellationToken cancellationToken = default);
}
