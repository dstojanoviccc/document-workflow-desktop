using System.IO.Compression;
using System.Xml.Linq;
using DocumentWorkflow.Application;
using DocumentWorkflow.Domain;
using DocumentWorkflow.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocumentWorkflow.Tests;

public sealed class ModificationWorkflowTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "workflow-modification-" + Guid.NewGuid().ToString("N"));
    private readonly DocumentRecord document = new("Product Catalog", "Product-Catalog.xlsx");
    private DbContextOptions<AppDbContext> Options => new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite($"Data Source={Path.Combine(root, "test.db")};Pooling=False").Options;
    private DocumentWorkflowService Service() => new(new WorkflowStore(new Factory(Options)),
        new DemoDocumentSource(Path.Combine(root, "source")), new LocalWorkspaceService(Path.Combine(root, "workspace"), Path.Combine(root, "source")),
        new FileHashService(), NullLogger<DocumentWorkflowService>.Instance, new NoOpener());
    private async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(root, "source"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "demo-data", document.FileName), Path.Combine(root, "source", document.FileName));
        await using var db = new AppDbContext(Options);
        await db.Database.MigrateAsync();
        db.Documents.Add(document);
        db.Versions.Add(new DocumentVersion(document.Id, 1, "initial", "Initial demo"));
        await db.SaveChangesAsync();
    }
    [Fact]
    public async Task Valid_binary_edit_restart_and_exact_restoration_are_derived_without_database_mutations()
    {
        await InitializeAsync();
        var service = Service();
        await service.CheckOutAsync(document.Id);
        var originalSnapshot = Assert.Single(await service.ListAsync());
        Assert.Equal(WorkingCopyState.Unchanged, originalSnapshot.Evaluation!.State);
        Assert.Equal(WorkingCopyState.Unchanged, Assert.Single(await Service().ListAsync()).Evaluation!.State);
        var path = originalSnapshot.WorkingCopy!.LocalPath;
        var originalBytes = await File.ReadAllBytesAsync(path);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!;
            XDocument xml;
            using (var read = entry.Open()) xml = XDocument.Load(read);
            var text = xml.Descendants().First(x => x.Name.LocalName == "v");
            text.Value += " edited";
            var name = entry.FullName;
            entry.Delete();
            using var write = zip.CreateEntry(name).Open();
            xml.Save(write);
        }
        using (var zip = ZipFile.OpenRead(path)) Assert.NotNull(zip.GetEntry("xl/workbook.xml"));
        var modified = Assert.Single(await service.ListAsync());
        Assert.Equal(WorkingCopyState.Modified, modified.Evaluation!.State);
        Assert.NotEqual(modified.WorkingCopy!.LastKnownHash, modified.Evaluation.CurrentHash);
        await Service().ReconcileAsync();
        Assert.Equal(WorkingCopyState.Modified, Assert.Single(await Service().ListAsync()).Evaluation!.State);
        await File.WriteAllBytesAsync(path, originalBytes);
        Assert.Equal(WorkingCopyState.Unchanged, Assert.Single(await service.ListAsync()).Evaluation!.State);
        await using var db = new AppDbContext(Options);
        Assert.Equal(WorkingCopyState.CheckedOut, (await db.WorkingCopies.SingleAsync()).State);
        Assert.Equal(originalSnapshot.WorkingCopy.LastKnownHash, (await db.WorkingCopies.SingleAsync()).LastKnownHash);
        Assert.Equal(1, await db.Versions.CountAsync());
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(Path.Combine(root, "source", document.FileName)));
    }
    private sealed class Factory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    { public AppDbContext CreateDbContext() => new(options); }
    private sealed class NoOpener : IWorkingCopyOpener { public void Open(string path) { } }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
