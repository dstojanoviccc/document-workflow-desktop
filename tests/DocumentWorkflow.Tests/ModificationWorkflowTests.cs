using System.IO.Compression;
using System.Xml.Linq;
using DocumentWorkflow.App;
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
    [Fact]
    public async Task Modified_discard_requires_stronger_confirmation_and_cancellation_preserves_changed_bytes()
    {
        await InitializeAsync();
        var service = Service();
        await service.CheckOutAsync(document.Id);
        var dialogs = new RecordingDialogs();
        var vm = new MainViewModel(service, dialogs, NullLogger<MainViewModel>.Instance);
        await vm.RefreshAsync();
        var row = Assert.Single(vm.Documents);
        Assert.Equal("Unchanged", row.Status);
        row.ViewDetailsCommand.Execute(null);
        var sourceBytes = await File.ReadAllBytesAsync(Path.Combine(root, "source", document.FileName));
        EditWorkbook(row.LocalPath);
        var changedBytes = await File.ReadAllBytesAsync(row.LocalPath);
        await vm.RefreshAsync();
        row = Assert.Single(vm.Documents);
        Assert.Equal("Modified", row.Status);
        Assert.True(row.IsModified);
        Assert.True(row.CanOpen);
        Assert.False(row.CanCheckOut);
        Assert.NotEqual(row.BaseHash, row.CurrentHash);
        Assert.Contains("differ", row.ContentsComparison);
        Assert.Equal(row.CurrentHash, vm.SelectedDocument!.CurrentHash);
        await Assert.ThrowsAsync<WorkflowException>(() => service.DiscardAsync(document.Id));
        await row.DiscardCommand.ExecuteAsync();
        Assert.True(dialogs.Warned);
        Assert.Contains("permanently delete local edits", UserDialogService.ConfirmationText(row.FileName, dialogs.Warned));
        Assert.Equal(changedBytes, await File.ReadAllBytesAsync(row.LocalPath));
        Assert.Equal("Modified", Assert.Single(vm.Documents).Status);
        Assert.NotNull(Assert.Single(await service.ListAsync()).WorkingCopy);
        dialogs.Confirm = true;
        await Assert.Single(vm.Documents).DiscardCommand.ExecuteAsync();
        Assert.False(File.Exists(row.LocalPath));
        Assert.Equal("Available", Assert.Single(vm.Documents).Status);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(Path.Combine(root, "source", document.FileName)));
        await using var db = new AppDbContext(Options);
        Assert.Equal(1, await db.Versions.CountAsync());
        Assert.Empty(await db.WorkingCopies.ToListAsync());
    }
    [Fact]
    public async Task Edit_during_unchanged_confirmation_is_rejected_until_a_stronger_warning_is_accepted()
    {
        await InitializeAsync();
        var service = Service();
        await service.CheckOutAsync(document.Id);
        var path = Assert.Single(await service.ListAsync()).WorkingCopy!.LocalPath;
        var dialogs = new RecordingDialogs { Confirm = true, OnConfirm = () => EditWorkbook(path) };
        var vm = new MainViewModel(service, dialogs, NullLogger<MainViewModel>.Instance);
        await vm.RefreshAsync();
        await Assert.Single(vm.Documents).DiscardCommand.ExecuteAsync();
        Assert.False(dialogs.Warned);
        Assert.True(File.Exists(path));
        Assert.Equal("Modified", Assert.Single(vm.Documents).Status);
        Assert.Contains("explicitly confirm", vm.Message);
        Assert.NotNull(Assert.Single(await service.ListAsync()).WorkingCopy);
    }
    private static void EditWorkbook(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!;
        XDocument xml;
        using (var read = entry.Open()) xml = XDocument.Load(read);
        xml.Descendants().First(x => x.Name.LocalName == "v").Value += " edited";
        var name = entry.FullName;
        entry.Delete();
        using var write = zip.CreateEntry(name).Open();
        xml.Save(write);
    }
    private sealed class RecordingDialogs : IUserDialogService
    {
        public bool Confirm { get; set; }
        public bool Warned { get; private set; }
        public Action? OnConfirm { get; set; }
        public bool ConfirmDiscard(string name, bool localEditsMayBeLost)
        { Warned = localEditsMayBeLost; OnConfirm?.Invoke(); return Confirm; }
    }
    private sealed class Factory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    { public AppDbContext CreateDbContext() => new(options); }
    private sealed class NoOpener : IWorkingCopyOpener { public void Open(string path) { } }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
