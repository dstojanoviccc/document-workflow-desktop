using DocumentWorkflow.Domain;
using DocumentWorkflow.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DocumentWorkflow.Tests;

public class PersistenceTests
{
    [Fact]
    public async Task Migration_persists_documents_versions_and_working_copy_metadata()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var document = new DocumentRecord("Guide", "Guide.pdf");
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.Documents.Add(document);
            db.Versions.Add(new DocumentVersion(document.Id, 1, "hash", "Initial version"));
            db.WorkingCopies.Add(new WorkingCopy(document.Id, "workspace/Guide.pdf", 1, "hash"));
            await db.SaveChangesAsync();
        }
        await using var read = new AppDbContext(options);
        Assert.Equal("Guide.pdf", (await read.Documents.SingleAsync()).FileName);
        Assert.Equal("Initial version", (await read.Versions.SingleAsync()).ChangeNote);
        Assert.Equal(WorkingCopyState.CheckedOut, (await read.WorkingCopies.SingleAsync()).State);
        Assert.Equal(document.Id, (await new DocumentRepository(new TestFactory(options)).ListAsync()).Single().Id);
    }

    [Fact]
    public async Task Duplicate_document_version_is_rejected()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.MigrateAsync();
        var document = new DocumentRecord("Guide", "Guide.pdf");
        db.Documents.Add(document);
        db.Versions.AddRange(new DocumentVersion(document.Id, 1, "hash", ""), new DocumentVersion(document.Id, 1, "hash2", ""));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private sealed class TestFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
