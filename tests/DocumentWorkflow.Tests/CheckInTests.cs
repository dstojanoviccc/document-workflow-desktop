using DocumentWorkflow.Application;
using DocumentWorkflow.Domain;
using DocumentWorkflow.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocumentWorkflow.Tests;

public sealed class CheckInTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "check-in-tests-" + Guid.NewGuid().ToString("N"));
    private readonly DocumentRecord document = new("Catalog", "Product-Catalog.xlsx");
    private readonly FileHashService hashes = new();
    private DbContextOptions<AppDbContext> Options => new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={Path.Combine(root, "test.db")};Pooling=False").Options;
    private string SourceRoot => Path.Combine(root, "source");
    private LocalWorkspaceService Workspace => new(Path.Combine(root, "workspace"), SourceRoot);
    private LocalVersionContentStore Versions => new(Path.Combine(root, "versions"), Workspace.Root, SourceRoot, hashes);
    private DocumentWorkflowService Service(IWorkflowStore? store = null, IVersionContentStore? content = null) => new(
        store ?? new WorkflowStore(new Factory(Options)), new DemoDocumentSource(SourceRoot), Workspace, hashes,
        NullLogger<DocumentWorkflowService>.Instance, new NoOpener(), versions: content ?? Versions);
    private async Task<string> InitializeAsync(bool edit = true)
    {
        Directory.CreateDirectory(SourceRoot);
        var source = Path.Combine(SourceRoot, document.FileName);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "demo-data", document.FileName), source);
        await using var db = new AppDbContext(Options);
        await db.Database.MigrateAsync();
        db.Documents.Add(document);
        db.Versions.Add(new(document.Id, 1, await hashes.HashAsync(source), "Initial"));
        await db.SaveChangesAsync();
        await Service().CheckOutAsync(document.Id);
        var path = Workspace.Resolve(document.Id, document.FileName);
        if (edit)
        {
            using var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Update);
            var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!;
            System.Xml.Linq.XDocument xml;
            using (var input = entry.Open()) xml = System.Xml.Linq.XDocument.Load(input);
            xml.Descendants().First(x => x.Name.LocalName == "v").Value += " checked in";
            var name = entry.FullName;
            entry.Delete();
            using var output = zip.CreateEntry(name).Open();
            xml.Save(output);
        }
        return path;
    }
    [Fact]
    public async Task Check_in_publishes_exactly_one_version_survives_restart_and_next_checkout_uses_new_bytes()
    {
        var path = await InitializeAsync();
        var original = await File.ReadAllBytesAsync(Path.Combine(SourceRoot, document.FileName));
        var edited = await File.ReadAllBytesAsync(path);
        Assert.Null(await Service().CheckInAsync(document.Id));
        await using var db = new AppDbContext(Options);
        var versions = await db.Versions.OrderBy(x => x.VersionNumber).ToListAsync();
        Assert.Equal(2, versions.Count);
        Assert.Equal(1, versions[1].BaseVersion);
        Assert.Equal(await hashes.HashAsync(Versions.Resolve(document.Id, 2, document.FileName)), versions[1].FileHash);
        Assert.Equal(edited, await File.ReadAllBytesAsync(Versions.Resolve(document.Id, 2, document.FileName)));
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(SourceRoot, document.FileName)));
        Assert.False(File.Exists(path));
        var restarted = Assert.Single(await Service().ListAsync());
        Assert.Equal(2, restarted.Document.CurrentVersion);
        Assert.Equal(WorkingCopyState.Available, restarted.Document.Status);
        Assert.Null(restarted.WorkingCopy);
        await Assert.ThrowsAsync<WorkflowException>(() => Service().CheckInAsync(document.Id));
        await Service().CheckOutAsync(document.Id);
        Assert.Equal(edited, await File.ReadAllBytesAsync(path));
        var newCheckout = Assert.Single(await Service().ListAsync());
        Assert.Equal(2, newCheckout.WorkingCopy!.BaseVersion);
        Assert.Equal(WorkingCopyState.Unchanged, newCheckout.Evaluation!.State);
    }
    [Theory]
    [InlineData("unchanged")]
    [InlineData("missing")]
    [InlineData("locked")]
    [InlineData("destination")]
    [InlineData("database")]
    public async Task Failures_do_not_publish_versions_or_destroy_checkout(string failure)
    {
        var path = await InitializeAsync(edit: failure != "unchanged");
        var edited = await File.ReadAllBytesAsync(path);
        FileStream? locked = null;
        if (failure == "missing") File.Delete(path);
        if (failure == "locked") locked = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (failure == "destination") await File.WriteAllTextAsync(Path.Combine(root, "versions"), "blocked directory");
        var service = Service(failure == "database" ? new FailingStore(new WorkflowStore(new Factory(Options))) : null);
        try { await Assert.ThrowsAsync<WorkflowException>(() => service.CheckInAsync(document.Id)); }
        finally { locked?.Dispose(); }
        await using var db = new AppDbContext(Options);
        Assert.Equal(1, await db.Versions.CountAsync());
        Assert.Equal(1, (await db.Documents.SingleAsync()).CurrentVersion);
        Assert.Single(await db.WorkingCopies.ToListAsync());
        if (failure != "missing") Assert.Equal(edited, await File.ReadAllBytesAsync(path));
        if (failure == "database")
        {
            Assert.False(File.Exists(Versions.Resolve(document.Id, 2, document.FileName)));
            Assert.False(File.Exists(path + ".discard"));
            Assert.Null(await Service().CheckInAsync(document.Id));
        }
    }
    [Fact]
    public async Task Competing_instances_create_only_one_version()
    {
        await InitializeAsync();
        async Task<bool> Run()
        {
            try { await Service().CheckInAsync(document.Id); return true; }
            catch (WorkflowException) { return false; }
        }
        var results = await Task.WhenAll(Run(), Run());
        Assert.Single(results, value => value);
        await using var db = new AppDbContext(Options);
        Assert.Equal(2, await db.Versions.CountAsync());
        Assert.Empty(await db.WorkingCopies.ToListAsync());
    }
    private sealed class Factory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    { public AppDbContext CreateDbContext() => new(options); }
    private sealed class NoOpener : IWorkingCopyOpener { public void Open(string path) { } }
    private sealed class FailingStore(IWorkflowStore inner) : IWorkflowStore
    {
        public Task<IReadOnlyList<DocumentSnapshot>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
        public async Task<IWorkflowSession> BeginAsync(Guid id, CancellationToken cancellationToken = default) => new Session(await inner.BeginAsync(id, cancellationToken));
        private sealed class Session(IWorkflowSession inner) : IWorkflowSession
        {
            public DocumentRecord Document => inner.Document;
            public WorkingCopy? WorkingCopy => inner.WorkingCopy;
            public void Add(WorkingCopy copy) => inner.Add(copy);
            public void AddVersion(DocumentVersion version) => inner.AddVersion(version);
            public void RemoveWorkingCopy() => inner.RemoveWorkingCopy();
            public Task CommitAsync(CancellationToken cancellationToken = default) => throw new IOException("Injected metadata failure");
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
