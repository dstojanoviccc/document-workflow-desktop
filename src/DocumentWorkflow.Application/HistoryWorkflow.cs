using DocumentWorkflow.Domain;

namespace DocumentWorkflow.Application;

public sealed partial class DocumentWorkflowService
{
    public async Task<DocumentHistoryDetails> GetHistoryAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var history = await store.GetHistoryAsync(documentId, cancellationToken).ConfigureAwait(false);
        var context = history.Context;
        if (context.WorkingCopy is not null)
        {
            var evaluation = await states.EvaluateAsync(context.Document, context.WorkingCopy, cancellationToken).ConfigureAwait(false);
            context = context with { Evaluation = evaluation, WorkspaceWarning = evaluation.Warning };
        }
        return new(context, history.Versions.Select(version => new VersionHistoryItem(version,
            version.VersionNumber == context.Document.CurrentVersion, ResolveVersion(context.Document, version.VersionNumber))).ToArray(), history.Events);
    }

    private string ResolveVersion(DocumentRecord document, int number) => number == 1 ? source.Resolve(document.FileName)
        : (versions ?? throw new WorkflowException("Version storage is not configured.")).Resolve(document.Id, number, document.FileName);

    public Task OpenVersionAsync(Guid documentId, int versionNumber, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        try
        {
            await using var session = await store.BeginAsync(documentId, cancellationToken);
            var version = session.Versions.SingleOrDefault(x => x.VersionNumber == versionNumber)
                ?? throw new WorkflowException("This historical version does not exist. Refresh history.");
            var inspection = await workspace.CreateInspectionAsync(documentId, versionNumber, session.Document.FileName,
                ResolveVersion(session.Document, versionNumber), cancellationToken);
            if (!string.Equals(await hashes.HashAsync(inspection, cancellationToken), version.FileHash, StringComparison.OrdinalIgnoreCase))
                throw new WorkflowException("Historical content does not match its recorded SHA-256. The inspection copy was not opened.");
            opener.Open(inspection);
        }
        catch (Exception error) when (error is not WorkflowException and not OperationCanceledException)
        {
            throw new WorkflowException("The historical version could not be opened. Check storage permissions and the default application for this file type.", error);
        }
    }, cancellationToken);
}
