using DocumentWorkflow.Application;
using DocumentWorkflow.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocumentWorkflow.Infrastructure;

public sealed class DocumentRepository(IDbContextFactory<AppDbContext> factory) : IDocumentRepository
{
    public async Task<IReadOnlyList<DocumentRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Documents.AsNoTracking().OrderBy(x => x.LogicalName).ToListAsync(cancellationToken);
    }
}
