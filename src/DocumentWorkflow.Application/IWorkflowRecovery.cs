using DocumentWorkflow.Domain;

namespace DocumentWorkflow.Application;

public interface IWorkflowRecovery
{
    Task<string?> RecoverAsync(DocumentRecord document, WorkingCopy? copy, IReadOnlyList<DocumentVersion> versions,
        CancellationToken cancellationToken = default);
    async Task<RecoveryReport> RecoverWithReportAsync(DocumentRecord document, WorkingCopy? copy, IReadOnlyList<DocumentVersion> versions,
        CancellationToken cancellationToken = default) => new(await RecoverAsync(document, copy, versions, cancellationToken), []);
}
public sealed record RecoveryReport(string? Warning, IReadOnlyList<string> Actions);
