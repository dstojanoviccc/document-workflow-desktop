using DocumentWorkflow.Domain;
using Microsoft.Extensions.Logging;

namespace DocumentWorkflow.Application;

public sealed partial class DocumentWorkflowService
{
    // Trusted local logical-writer operation, intentionally not exposed as arbitrary UI ingestion.
    public Task PublishCompetingVersionAsync(Guid documentId, string contentPath, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            var content = versions ?? throw new WorkflowException("Version storage is not configured.");
            await using var session = await store.BeginAsync(documentId, cancellationToken);
            var document = session.Document;
            if (session.WorkingCopy is { } copy && string.Equals(Path.GetFullPath(contentPath), Path.GetFullPath(copy.LocalPath), StringComparison.OrdinalIgnoreCase))
                throw new WorkflowException("The competing writer must use its own content, not this active checkout.");
            var next = checked(document.CurrentVersion + 1);
            var created = false;
            try
            {
                var hash = await hashes.HashAsync(contentPath, cancellationToken);
                var actual = await content.CreateAsync(documentId, next, document.FileName, contentPath, hash, cancellationToken);
                created = true;
                var version = new DocumentVersion(documentId, next, actual, "Competing local writer.", document.CurrentVersion);
                session.AddVersion(version);
                document.PublishCompetingVersion(document.CurrentVersion, next, version.CreatedAt);
                await session.CommitAsync(cancellationToken);
                logger.LogInformation("Competing writer published {DocumentId} version {VersionNumber}", documentId, next);
            }
            catch
            {
                if (created) content.Delete(documentId, next, document.FileName);
                throw;
            }
        }, cancellationToken);
}
