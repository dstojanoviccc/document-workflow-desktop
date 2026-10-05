using DocumentWorkflow.Application;
using DocumentWorkflow.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocumentWorkflow.Infrastructure;

public sealed class DatabaseInitializer(IDbContextFactory<AppDbContext> factory, IDocumentSource source,
    IFileHashService hashes, ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await db.Documents.AnyAsync(cancellationToken))
        {
            string[] names = ["Product-Catalog.xlsx", "Supplier-Agreement.docx", "Installation-Guide.pdf", "Pricing-Overview.xlsx", "Technical-Specification.docx"];
            foreach (var name in names)
            {
                var document = new DocumentRecord(Path.GetFileNameWithoutExtension(name).Replace('-', ' '), name);
                db.Documents.Add(document);
                db.Versions.Add(new DocumentVersion(document.Id, 1, "pending-demo-content", "Demo metadata — awaiting source file."));
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        // Upgrade only Phase 1 metadata placeholders. Preserve IDs, version numbers and existing checkouts.
        var placeholders = await (from version in db.Versions
                                  join document in db.Documents on version.DocumentId equals document.Id
                                  where version.VersionNumber == 1 && version.ChangeNote.StartsWith("Demo metadata")
                                  select new { Version = version, document.FileName }).ToListAsync(cancellationToken);
        foreach (var item in placeholders)
        {
            try { item.Version.AttachDemoContent(await hashes.HashAsync(source.Resolve(item.FileName), cancellationToken)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { logger.LogWarning(error, "Demo source unavailable for {FileName}; checkout will report the source error", item.FileName); }
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
