using DocumentWorkflow.Domain;
using Microsoft.Extensions.Logging;

namespace DocumentWorkflow.Application;

public sealed partial class DocumentWorkflowService
{
    public Task SaveLocalCopyAsync(Guid documentId, string destination, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        try
        {
            await using var session = await store.BeginAsync(documentId, cancellationToken);
            var copy = session.WorkingCopy ?? throw new WorkflowException("There is no active local copy to save.");
            if (versions?.IsManagedPath(destination) == true) throw new WorkflowException("Save the copy outside managed version storage.");
            if (!workspace.Exists(documentId, session.Document.FileName, copy.LocalPath)) throw new WorkflowException("The local file is missing. No copy was saved.");
            await workspace.ExportAsync(documentId, session.Document.FileName, copy.LocalPath, destination, cancellationToken);
            logger.LogInformation("Local recovery copy saved for {DocumentId}", documentId);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { throw new WorkflowException("The local copy could not be saved. Choose a new writable file path; existing files are never overwritten. Your checkout remains intact.", error); }
    }, cancellationToken);

    public Task OpenLatestAsync(Guid documentId, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        try
        {
            await using var session = await store.BeginAsync(documentId, cancellationToken);
            var document = session.Document;
            var current = document.CurrentVersion == 1 ? source.Resolve(document.FileName)
                : (versions ?? throw new WorkflowException("Version storage is not configured.")).Resolve(documentId, document.CurrentVersion, document.FileName);
            var path = await workspace.CreateInspectionAsync(documentId, document.CurrentVersion, document.FileName, current, cancellationToken);
            opener.Open(path);
            logger.LogInformation("Opened latest inspection copy for {DocumentId} at version {VersionNumber}", documentId, document.CurrentVersion);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        { throw new WorkflowException("The latest version could not be opened. Check storage permissions and the Windows file association. Your local edits have been kept.", error); }
    }, cancellationToken);

    // Trusted local logical-writer operation, intentionally not exposed as arbitrary UI ingestion.
    public Task PublishCompetingVersionAsync(Guid documentId, string contentPath, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            var content = versions ?? throw new WorkflowException("Version storage is not configured.");
            DocumentRecord? document = null;
            var next = 0;
            var created = false;
            try
            {
                await using var session = await store.BeginAsync(documentId, cancellationToken);
                document = session.Document;
                if (session.WorkingCopy is { } copy && string.Equals(Path.GetFullPath(contentPath), Path.GetFullPath(copy.LocalPath), StringComparison.OrdinalIgnoreCase))
                    throw new WorkflowException("The competing writer must use its own content, not this active checkout.");
                next = checked(document.CurrentVersion + 1);
                var hash = await hashes.HashAsync(contentPath, cancellationToken);
                var actual = await content.CreateAsync(documentId, next, document.FileName, contentPath, hash, cancellationToken);
                created = true;
                var version = new DocumentVersion(documentId, next, actual, "Competing local writer.", document.CurrentVersion);
                session.AddVersion(version);
                document.PublishCompetingVersion(document.CurrentVersion, next, version.CreatedAt);
                await session.CommitAsync(cancellationToken);
                logger.LogInformation("Competing writer published {DocumentId} version {VersionNumber}", documentId, next);
            }
            catch (Exception error)
            {
                if (created)
                {
                    await using var verification = await store.BeginAsync(documentId, CancellationToken.None);
                    if (verification.Versions.Any(x => x.VersionNumber == next))
                    {
                        logger.LogWarning(error, "Competing publication confirmed from metadata after completion error for {DocumentId}", documentId);
                        return;
                    }
                    content.Delete(documentId, next, document!.FileName);
                }
                throw;
            }
        }, cancellationToken);
}
