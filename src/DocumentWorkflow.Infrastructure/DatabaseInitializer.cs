using System.Security.Cryptography;
using System.Text;
using DocumentWorkflow.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocumentWorkflow.Infrastructure;

public sealed class DatabaseInitializer(IDbContextFactory<AppDbContext> factory)
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
                // These are metadata fixtures, not hashes of downloadable document content.
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"demo-metadata:{name}")));
                db.Versions.Add(new DocumentVersion(document.Id, 1, hash, "Demo metadata — initial version; no content file supplied."));
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
