using DocumentWorkflow.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DocumentWorkflow.Tests;

public class SeedTests
{
    [Fact]
    public async Task File_database_reopens_without_duplicate_seed_data()
    {
        var path = Path.Combine(Path.GetTempPath(), $"workflow-test-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
        try
        {
            var initializer = new DatabaseInitializer(new TestFactory(options), new DemoDocumentSource(Path.Combine(AppContext.BaseDirectory, "demo-data")), new FileHashService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseInitializer>.Instance);
            await initializer.InitializeAsync(seedDemo: true);
            await initializer.InitializeAsync(seedDemo: true);
            await using var db = new AppDbContext(options);
            Assert.Equal(5, await db.Documents.CountAsync());
            Assert.Equal(5, await db.Versions.CountAsync());
            Assert.Empty(await db.WorkingCopies.ToListAsync());
            var documents = await new DocumentRepository(new TestFactory(options)).ListAsync();
            Assert.Equal("Installation Guide", documents[0].LogicalName);
            Assert.Contains(documents, x => x.FileName == "Supplier-Agreement.docx");
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task Phase_one_placeholder_is_upgraded_without_changing_identity_or_versions()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var document = new DocumentWorkflow.Domain.DocumentRecord("Product Catalog", "Product-Catalog.xlsx");
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.Documents.Add(document);
            db.Versions.Add(new DocumentWorkflow.Domain.DocumentVersion(document.Id, 1, "metadata-placeholder", "Demo metadata — initial version; no content file supplied."));
            await db.SaveChangesAsync();
        }
        var source = new DemoDocumentSource(Path.Combine(AppContext.BaseDirectory, "demo-data"));
        var hashes = new FileHashService();
        await new DatabaseInitializer(new TestFactory(options), source, hashes,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseInitializer>.Instance).InitializeAsync();
        await using var read = new AppDbContext(options);
        Assert.Equal(document.Id, (await read.Documents.SingleAsync()).Id);
        var version = await read.Versions.SingleAsync();
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal(await hashes.HashAsync(source.Resolve(document.FileName)), version.FileHash);
        Assert.Equal("Initial generic demo file.", version.ChangeNote);
    }
    private sealed class TestFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
