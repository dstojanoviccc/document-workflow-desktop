using DocumentWorkflow.Application;
using DocumentWorkflow.Domain;
using DocumentWorkflow.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocumentWorkflow.Tests;

public sealed class WorkflowTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "workflow-tests-" + Guid.NewGuid().ToString("N"));
    private readonly DocumentRecord document = new("Generic guide", "guide.txt");
    private DbContextOptions<AppDbContext> Options => new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={Path.Combine(root, "test.db")};Pooling=False").Options;
    private string SourcePath => Path.Combine(root, "source", "guide.txt");
    private LocalWorkspaceService Workspace => new(Path.Combine(root, "workspace"), Path.Combine(root, "source"));
    private readonly RecordingOpener opener = new();
    private DocumentWorkflowService Service(IWorkflowStore? store = null) => new(store ?? new WorkflowStore(new Factory(Options)),
        new DemoDocumentSource(Path.Combine(root, "source")), Workspace, new FileHashService(), NullLogger<DocumentWorkflowService>.Instance, opener);
    private async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SourcePath)!);
        await File.WriteAllTextAsync(SourcePath, "Generic guide content.");
        await using var db = new AppDbContext(Options);
        await db.Database.MigrateAsync();
        db.Documents.Add(document);
        db.Versions.Add(new DocumentVersion(document.Id, 1, await new FileHashService().HashAsync(SourcePath), "Initial"));
        await db.SaveChangesAsync();
    }
    [Fact]
    public async Task Checkout_copies_persists_baseline_and_survives_service_recreation()
    {
        await InitializeAsync();
        await Service().CheckOutAsync(document.Id);
        var snapshot = Assert.Single(await Service().ListAsync());
        Assert.Equal(WorkingCopyState.CheckedOut, snapshot.Document.Status);
        var copy = Assert.IsType<WorkingCopy>(snapshot.WorkingCopy);
        Assert.Equal(document.CurrentVersion, copy.BaseVersion);
        Assert.Equal(await new FileHashService().HashAsync(SourcePath), copy.LastKnownHash);
        Assert.Equal(await File.ReadAllTextAsync(SourcePath), await File.ReadAllTextAsync(copy.LocalPath));
        Assert.Null(snapshot.WorkspaceWarning);
        await Service().OpenAsync(document.Id);
        Assert.Equal(copy.LocalPath, opener.Path);
        await Service().ReconcileAsync();
    }
    [Fact]
    public async Task Duplicate_checkout_is_rejected_without_overwriting_local_edits()
    {
        await InitializeAsync();
        await Service().CheckOutAsync(document.Id);
        var path = Workspace.Resolve(document.Id, document.FileName);
        await File.WriteAllTextAsync(path, "local edit");
        await Assert.ThrowsAsync<WorkflowException>(() => Service().CheckOutAsync(document.Id));
        Assert.Equal("local edit", await File.ReadAllTextAsync(path));
        await using var db = new AppDbContext(Options);
        Assert.Equal(1, await db.WorkingCopies.CountAsync());
    }
    [Fact]
    public async Task Missing_source_does_not_change_metadata_or_create_a_file()
    {
        await InitializeAsync();
        File.Delete(SourcePath);
        await Assert.ThrowsAsync<WorkflowException>(() => Service().CheckOutAsync(document.Id));
        var snapshot = Assert.Single(await Service().ListAsync());
        Assert.Equal(WorkingCopyState.Available, snapshot.Document.Status);
        Assert.Null(snapshot.WorkingCopy);
        Assert.False(File.Exists(Workspace.Resolve(document.Id, document.FileName)));
    }
    [Fact]
    public async Task Discard_deletes_only_local_copy_and_preserves_version_history()
    {
        await InitializeAsync();
        await Service().CheckOutAsync(document.Id);
        Assert.Null(await Service().DiscardAsync(document.Id));
        var snapshot = Assert.Single(await Service().ListAsync());
        Assert.Equal(WorkingCopyState.Available, snapshot.Document.Status);
        Assert.Null(snapshot.WorkingCopy);
        Assert.True(File.Exists(SourcePath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(Workspace.Resolve(document.Id, document.FileName))));
        await using var db = new AppDbContext(Options);
        Assert.Equal(1, await db.Versions.CountAsync());
        Assert.Equal(1, snapshot.Document.CurrentVersion);
        await Service().CheckOutAsync(document.Id);
    }
    [Fact]
    public async Task Open_and_discard_without_checkout_are_rejected()
    {
        await InitializeAsync();
        await Assert.ThrowsAsync<WorkflowException>(() => Service().DiscardAsync(document.Id));
        await Assert.ThrowsAsync<WorkflowException>(() => Service().OpenAsync(document.Id));
        Assert.Null(opener.Path);
    }
    [Fact]
    public async Task Missing_local_file_is_warned_and_can_be_discarded_without_recreation()
    {
        await InitializeAsync();
        await Service().CheckOutAsync(document.Id);
        var path = Workspace.Resolve(document.Id, document.FileName);
        File.Delete(path);
        await Service().ReconcileAsync();
        Assert.NotNull(Assert.Single(await Service().ListAsync()).WorkspaceWarning);
        await Assert.ThrowsAsync<WorkflowException>(() => Service().OpenAsync(document.Id));
        Assert.False(File.Exists(path));
        await Service().DiscardAsync(document.Id);
        Assert.Null(Assert.Single(await Service().ListAsync()).WorkingCopy);
    }
    [Fact]
    public async Task Locked_local_file_keeps_checkout_intact_when_discard_fails()
    {
        await InitializeAsync();
        await Service().CheckOutAsync(document.Id);
        var path = Workspace.Resolve(document.Id, document.FileName);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAsync<WorkflowException>(() => Service().DiscardAsync(document.Id));
        Assert.True(File.Exists(path));
        Assert.NotNull(Assert.Single(await Service().ListAsync()).WorkingCopy);
    }
    [Fact]
    public async Task Persistence_failure_removes_new_checkout_file_and_restores_discard_file()
    {
        await InitializeAsync();
        var failing = new CommitFailingStore(new WorkflowStore(new Factory(Options)));
        await Assert.ThrowsAsync<WorkflowException>(() => Service(failing).CheckOutAsync(document.Id));
        var path = Workspace.Resolve(document.Id, document.FileName);
        Assert.False(File.Exists(path));
        Assert.Null(Assert.Single(await Service().ListAsync()).WorkingCopy);
        await Service().CheckOutAsync(document.Id);
        await Assert.ThrowsAsync<WorkflowException>(() => Service(failing).DiscardAsync(document.Id));
        Assert.True(File.Exists(path));
        Assert.NotNull(Assert.Single(await Service().ListAsync()).WorkingCopy);
    }
    private sealed class Factory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
    private sealed class RecordingOpener : IWorkingCopyOpener
    {
        public string? Path { get; private set; }
        public void Open(string path) => Path = path;
    }
    private sealed class CommitFailingStore(IWorkflowStore inner) : IWorkflowStore
    {
        public Task<IReadOnlyList<DocumentSnapshot>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
        public async Task<IWorkflowSession> BeginAsync(Guid id, CancellationToken cancellationToken = default) => new FailingSession(await inner.BeginAsync(id, cancellationToken));
        private sealed class FailingSession(IWorkflowSession session) : IWorkflowSession
        {
            public DocumentRecord Document => session.Document;
            public WorkingCopy? WorkingCopy => session.WorkingCopy;
            public void Add(WorkingCopy copy) => session.Add(copy);
            public void RemoveWorkingCopy() => session.RemoveWorkingCopy();
            public Task CommitAsync(CancellationToken cancellationToken = default) => throw new IOException("Simulated persistence failure.");
            public ValueTask DisposeAsync() => session.DisposeAsync();
        }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
