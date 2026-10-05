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
            await initializer.InitializeAsync();
            await initializer.InitializeAsync();
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
    private sealed class TestFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}

