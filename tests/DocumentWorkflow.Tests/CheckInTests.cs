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
        await File.AppendAllTextAsync(path, "third version edit");
        Assert.Null(await Service().CheckInAsync(document.Id));
        await using var reopened = new AppDbContext(Options);
        var third = await reopened.Versions.SingleAsync(x => x.VersionNumber == 3);
        Assert.Equal(2, third.BaseVersion);
        Assert.Equal(3, (await reopened.Documents.SingleAsync()).CurrentVersion);
        Assert.Equal(edited, await File.ReadAllBytesAsync(Versions.Resolve(document.Id, 2, document.FileName)));
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(SourceRoot, document.FileName)));
        Assert.Equal(versions[0].FileHash, (await reopened.Versions.SingleAsync(x => x.VersionNumber == 1)).FileHash);
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

    [Theory]
    [InlineData("delete-before-copy")]
    [InlineData("edit-before-copy")]
    [InlineData("edit-after-copy")]
    public async Task Changes_after_evaluation_or_preparation_are_rejected_without_losing_edits(string fault)
    {
        var path = await InitializeAsync();
        var injected = new InterceptingContent(Versions, path, fault);
        await Assert.ThrowsAsync<WorkflowException>(() => Service(content: injected).CheckInAsync(document.Id));
        await using var db = new AppDbContext(Options);
        Assert.Equal(1, await db.Versions.CountAsync());
        Assert.Single(await db.WorkingCopies.ToListAsync());
        Assert.False(File.Exists(Versions.Resolve(document.Id, 2, document.FileName)));
        if (fault != "delete-before-copy") Assert.Equal("new edits during check-in", await File.ReadAllTextAsync(path));
    }
    [Fact]
    public async Task Actual_view_model_check_in_updates_row_and_rejects_duplicate_command_execution()
    {
        var path = await InitializeAsync(edit: false);
        var vm = new DocumentWorkflow.App.MainViewModel(Service(), new NoDialogs(), NullLogger<DocumentWorkflow.App.MainViewModel>.Instance);
        await vm.RefreshAsync();
        Assert.False(Assert.Single(vm.Documents).CheckInCommand.CanExecute(null));
        await File.AppendAllTextAsync(path, "local edit");
        await vm.RefreshAsync();
        var row = Assert.Single(vm.Documents);
        Assert.True(row.CanCheckIn);
        row.ViewDetailsCommand.Execute(null);
        await Task.WhenAll(row.CheckInCommand.ExecuteAsync(), row.CheckInCommand.ExecuteAsync());
        var completed = Assert.Single(vm.Documents);
        Assert.Equal("v2", completed.Version);
        Assert.Equal("Available", completed.Status);
        Assert.False(completed.CheckInCommand.CanExecute(null));
        Assert.False(completed.HasWorkingCopy);
        Assert.Equal("v2", vm.SelectedDocument!.Version);
        Assert.Contains("checked in as v2", vm.Message);
        Assert.False(vm.IsBusy);
        await using var db = new AppDbContext(Options);
        Assert.Equal(2, await db.Versions.CountAsync());
    }
    [Fact]
    public async Task Stale_modified_row_fails_safely_when_file_is_missing_and_command_remains_usable()
    {
        var path = await InitializeAsync();
        var vm = new DocumentWorkflow.App.MainViewModel(Service(), new NoDialogs(), NullLogger<DocumentWorkflow.App.MainViewModel>.Instance);
        await vm.RefreshAsync();
        var row = Assert.Single(vm.Documents);
        Assert.True(row.CheckInCommand.CanExecute(null));
        File.Delete(path);
        await row.CheckInCommand.ExecuteAsync();
        var missing = Assert.Single(vm.Documents);
        Assert.False(missing.CanCheckIn);
        Assert.True(missing.HasWorkingCopy);
        Assert.Contains("missing", vm.Message);
        Assert.False(vm.IsBusy);
        await File.WriteAllTextAsync(path, "recovered local edits");
        await vm.RefreshAsync();
        Assert.True(Assert.Single(vm.Documents).CanCheckIn);
    }
    private sealed class NoDialogs : DocumentWorkflow.App.IUserDialogService
    { public bool ConfirmDiscard(string name, bool edits) => false; }
    [Fact]
    public async Task Database_failure_after_sql_writes_rolls_back_version_and_checkout_together()
    {
        var path = await InitializeAsync();
        var edits = await File.ReadAllBytesAsync(path);
        var options = new DbContextOptionsBuilder<AppDbContext>(Options).AddInterceptors(new FailAfterSave()).Options;
        await Assert.ThrowsAsync<WorkflowException>(() => Service(new WorkflowStore(new Factory(options))).CheckInAsync(document.Id));
        await using var db = new AppDbContext(Options);
        Assert.Equal(1, await db.Versions.CountAsync());
        Assert.Equal(1, (await db.Documents.SingleAsync()).CurrentVersion);
        Assert.Single(await db.WorkingCopies.ToListAsync());
        Assert.Equal(edits, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(Versions.Resolve(document.Id, 2, document.FileName)));
    }
    private sealed class FailAfterSave : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default) => throw new IOException("Failure after SQL writes, before transaction commit");
    }
    [Theory]
    [InlineData(WorkingCopyState.Unchanged, EvaluationIssue.None, false)]
    [InlineData(WorkingCopyState.Modified, EvaluationIssue.None, true)]
    [InlineData(WorkingCopyState.Modified, EvaluationIssue.Unreadable, false)]
    [InlineData(null, EvaluationIssue.Missing, false)]
    public void Check_in_enablement_requires_verified_modified_content(WorkingCopyState? state, EvaluationIssue issue, bool enabled)
    {
        var copy = new WorkingCopy(document.Id, "isolated.xlsx", 1, "baseline");
        var evaluation = new WorkingCopyEvaluation(state, null, issue, issue == EvaluationIssue.None ? null : "Cannot evaluate");
        var row = new DocumentWorkflow.App.DocumentListItemViewModel(new(document, copy, evaluation.Warning, evaluation), _ => { }, (_, _) => Task.CompletedTask, _ => { });
        Assert.Equal(enabled, row.CanCheckIn);
        Assert.Equal(enabled, row.CheckInCommand.CanExecute(null));
    }
    private sealed class InterceptingContent(IVersionContentStore inner, string path, string fault) : IVersionContentStore
    {
        public string Resolve(Guid id, int version, string name) => inner.Resolve(id, version, name);
        public void Delete(Guid id, int version, string name) => inner.Delete(id, version, name);
        public async Task<string> CreateAsync(Guid id, int version, string name, string workingPath, string hash, CancellationToken cancellationToken = default)
        {
            if (fault == "delete-before-copy") File.Delete(path);
            if (fault == "edit-before-copy") await File.WriteAllTextAsync(path, "new edits during check-in", cancellationToken);
            var actual = await inner.CreateAsync(id, version, name, workingPath, hash, cancellationToken);
            if (fault == "edit-after-copy") await File.WriteAllTextAsync(path, "new edits during check-in", cancellationToken);
            return actual;
        }
    }
}
