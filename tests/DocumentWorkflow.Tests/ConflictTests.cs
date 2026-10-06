using System.IO.Compression;
using System.Xml.Linq;
using DocumentWorkflow.App;
using DocumentWorkflow.Application;
using DocumentWorkflow.Domain;
using DocumentWorkflow.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocumentWorkflow.Tests;

public sealed class ConflictTests : IDisposable
{
    private readonly ConflictEnvironment env = new();
    [Fact]
    public async Task Stale_modified_checkout_returns_structured_conflict_and_survives_restart()
    {
        await env.InitializeAsync();
        await env.CreateConflictAsync();
        var edited = await File.ReadAllBytesAsync(env.WorkingPath);
        var result = await env.Service().CheckInWithResultAsync(env.Document.Id);
        Assert.False(result.Succeeded);
        Assert.Equal(new CheckoutConflict(1, 2, env.WorkingPath, true), result.Conflict);
        var snapshot = Assert.Single(await env.Service().ListAsync());
        Assert.Equal(WorkingCopyState.Conflict, snapshot.Evaluation!.State);
        Assert.Equal(1, snapshot.WorkingCopy!.BaseVersion);
        Assert.Equal(2, snapshot.Document.CurrentVersion);
        Assert.Equal(edited, await File.ReadAllBytesAsync(env.WorkingPath));
        await using var db = env.Db();
        Assert.Equal(2, await db.Versions.CountAsync());
        Assert.False(File.Exists(env.Versions.Resolve(env.Document.Id, 3, env.Document.FileName)));
        var attempts = await Task.WhenAll(env.Service().CheckInWithResultAsync(env.Document.Id), env.Service().CheckInWithResultAsync(env.Document.Id));
        Assert.All(attempts, x => Assert.NotNull(x.Conflict));
        Assert.Equal(2, await db.Versions.CountAsync());
    }
    [Fact]
    public async Task Latest_and_export_preserve_exact_local_bytes_and_existing_destinations()
    {
        await env.InitializeAsync();
        await env.CreateConflictAsync();
        var edited = await File.ReadAllBytesAsync(env.WorkingPath);
        var service = env.Service();
        await service.OpenLatestAsync(env.Document.Id);
        var inspection = env.Opener.Path!;
        Assert.NotEqual(env.WorkingPath, inspection);
        Assert.True((File.GetAttributes(inspection) & FileAttributes.ReadOnly) != 0);
        Assert.Equal(await File.ReadAllBytesAsync(env.Versions.Resolve(env.Document.Id, 2, env.Document.FileName)), await File.ReadAllBytesAsync(inspection));
        var exported = Path.Combine(env.Root, "saved.xlsx");
        await service.SaveLocalCopyAsync(env.Document.Id, exported);
        Assert.Equal(edited, await File.ReadAllBytesAsync(exported));
        await Assert.ThrowsAsync<WorkflowException>(() => service.SaveLocalCopyAsync(env.Document.Id, exported));
        Assert.Equal(edited, await File.ReadAllBytesAsync(exported));
        await Assert.ThrowsAsync<WorkflowException>(() => service.SaveLocalCopyAsync(env.Document.Id, env.Versions.Resolve(env.Document.Id, 3, env.Document.FileName)));
        Assert.Equal(edited, await File.ReadAllBytesAsync(env.WorkingPath));
        Assert.Equal(WorkingCopyState.Conflict, Assert.Single(await env.Service().ListAsync()).Evaluation!.State);
    }
    [Fact]
    public async Task Recovery_commands_keep_cancel_export_and_confirm_discard_safely()
    {
        await env.InitializeAsync();
        await env.CreateConflictAsync();
        var edited = await File.ReadAllBytesAsync(env.WorkingPath);
        var dialogs = new Dialogs();
        var vm = new MainViewModel(env.Service(), dialogs, NullLogger<MainViewModel>.Instance);
        await vm.RefreshAsync();
        var row = Assert.Single(vm.Documents);
        Assert.Equal("Conflict", row.Status);
        Assert.False(row.CheckInCommand.CanExecute(null));
        Assert.True(row.CanOpen);
        Assert.Contains("edits still exist", row.ConflictExplanation);
        await row.KeepLocalCommand.ExecuteAsync();
        await row.SaveLocalCommand.ExecuteAsync();
        Assert.Contains("cancelled", vm.Message);
        await row.DiscardCommand.ExecuteAsync();
        Assert.True(dialogs.StrongWarning);
        Assert.Equal(edited, await File.ReadAllBytesAsync(env.WorkingPath));
        Assert.True(Assert.Single(vm.Documents).IsConflict);
        dialogs.Destination = Path.Combine(env.Root, "export.xlsx");
        await Assert.Single(vm.Documents).SaveLocalCommand.ExecuteAsync();
        Assert.Equal(edited, await File.ReadAllBytesAsync(dialogs.Destination));
        dialogs.Confirm = true;
        await Assert.Single(vm.Documents).DiscardCommand.ExecuteAsync();
        Assert.False(File.Exists(env.WorkingPath));
        Assert.Equal("Available", Assert.Single(vm.Documents).Status);
        Assert.Equal("v2", Assert.Single(vm.Documents).Version);
        await env.Service().CheckOutAsync(env.Document.Id);
        Assert.Equal(await File.ReadAllBytesAsync(env.Versions.Resolve(env.Document.Id, 2, env.Document.FileName)), await File.ReadAllBytesAsync(env.WorkingPath));
    }
    [Fact]
    public async Task Unchanged_stale_checkout_is_not_invented_as_a_modified_conflict()
    {
        await env.InitializeAsync();
        await env.PublishAsync();
        var snapshot = Assert.Single(await env.Service().ListAsync());
        Assert.Equal(WorkingCopyState.Unchanged, snapshot.Evaluation!.State);
        var result = await env.Service().CheckInWithResultAsync(env.Document.Id);
        Assert.False(result.Succeeded);
        Assert.False(result.Conflict!.HasLocalEdits);
        Assert.Equal(1, snapshot.WorkingCopy!.BaseVersion);
        await env.Service().DiscardAsync(env.Document.Id);
        Assert.Equal(2, Assert.Single(await env.Service().ListAsync()).Document.CurrentVersion);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_unreadable_stale_file_does_not_invent_edits(bool locked)
    {
        await env.InitializeAsync();
        await env.CreateConflictAsync();
        FileStream? handle = null;
        if (locked) handle = new(env.WorkingPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        else File.Delete(env.WorkingPath);
        try
        {
            var snapshot = Assert.Single(await env.Service().ListAsync());
            Assert.Null(snapshot.Evaluation!.State);
            Assert.Equal(locked ? EvaluationIssue.Unreadable : EvaluationIssue.Missing, snapshot.Evaluation.Issue);
            await Assert.ThrowsAsync<WorkflowException>(() => env.Service().CheckInWithResultAsync(env.Document.Id));
        }
        finally { handle?.Dispose(); }
        await using var db = env.Db();
        Assert.Single(await db.WorkingCopies.ToListAsync());
        Assert.Equal(2, await db.Versions.CountAsync());
    }
    [Fact]
    public async Task Competing_writer_cannot_publish_from_the_active_working_copy()
    {
        await env.InitializeAsync();
        await Assert.ThrowsAsync<WorkflowException>(() => env.Service().PublishCompetingVersionAsync(env.Document.Id, env.WorkingPath));
        Assert.Equal(1, Assert.Single(await env.Service().ListAsync()).Document.CurrentVersion);
    }
    private sealed class Dialogs : IUserDialogService
    {
        public bool Confirm { get; set; }
        public bool StrongWarning { get; private set; }
        public string? Destination { get; set; }
        public bool ConfirmDiscard(string fileName, bool localEditsMayBeLost) { StrongWarning = localEditsMayBeLost; return Confirm; }
        public string? SelectLocalCopyDestination(string fileName) => Destination;
    }
    public void Dispose() => env.Dispose();
}

internal sealed class ConflictEnvironment : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "conflict-tests-" + Guid.NewGuid().ToString("N"));
    public DocumentRecord Document { get; } = new("Catalog", "Product-Catalog.xlsx");
    public RecordingOpener Opener { get; } = new();
    public string SourceRoot => Path.Combine(Root, "source");
    public string SourcePath => Path.Combine(SourceRoot, Document.FileName);
    public LocalWorkspaceService Workspace => new(Path.Combine(Root, "workspace"), SourceRoot);
    public LocalVersionContentStore Versions => new(Path.Combine(Root, "versions"), Workspace.Root, SourceRoot, new FileHashService());
    public string WorkingPath => Workspace.Resolve(Document.Id, Document.FileName);
    public string RecoveryRoot => Path.Combine(Root, "recovery");
    public DbContextOptions<AppDbContext> Options => new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={Path.Combine(Root, "documents.db")};Pooling=False").Options;
    public AppDbContext Db() => new(Options);
    public DocumentWorkflowService Service(bool recover = true) => new(new WorkflowStore(new Factory(Options)), new DemoDocumentSource(SourceRoot), Workspace,
        new FileHashService(), NullLogger<DocumentWorkflowService>.Instance, Opener, versions: Versions,
        recovery: recover ? new LocalWorkflowRecovery(Workspace, Versions, RecoveryRoot, NullLogger<LocalWorkflowRecovery>.Instance) : null);
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(SourceRoot);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "demo-data", Document.FileName), SourcePath);
        await using var db = Db();
        await db.Database.MigrateAsync();
        db.Documents.Add(Document);
        db.Versions.Add(new(Document.Id, 1, await new FileHashService().HashAsync(SourcePath), "Initial"));
        await db.SaveChangesAsync();
        await Service().CheckOutAsync(Document.Id);
    }
    public async Task PublishAsync()
    {
        var writerPath = Path.Combine(Root, "writer.xlsx");
        File.Copy(SourcePath, writerPath, true);
        Edit(writerPath, "competing writer");
        await Service().PublishCompetingVersionAsync(Document.Id, writerPath);
    }
    public async Task CreateConflictAsync() { Edit(WorkingPath, "local edits"); await PublishAsync(); }
    public static void Edit(string path, string text)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!;
        XDocument xml;
        using (var input = entry.Open()) xml = XDocument.Load(input);
        xml.Descendants().First(x => x.Name.LocalName == "v").Value += " " + text;
        var name = entry.FullName;
        entry.Delete();
        using var output = zip.CreateEntry(name).Open();
        xml.Save(output);
    }
    public sealed class RecordingOpener : IWorkingCopyOpener { public string? Path { get; private set; } public void Open(string path) => Path = path; }
    private sealed class Factory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    { public AppDbContext CreateDbContext() => new(options); }
    public void Dispose()
    {
        if (!Directory.Exists(Root)) return;
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(Root, true);
    }
}
