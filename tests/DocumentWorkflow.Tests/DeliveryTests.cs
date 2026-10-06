using System.Diagnostics;
using System.Reflection;
using DocumentWorkflow.App;
using DocumentWorkflow.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocumentWorkflow.Tests;

public sealed class DeliveryTests
{
    [Fact]
    public void Default_user_data_is_independent_of_binary_location_across_upgrade()
    {
        var first = RuntimePaths.Resolve(Path.Combine(Path.GetTempPath(), "build-a"));
        var upgraded = RuntimePaths.Resolve(Path.Combine(Path.GetTempPath(), "build-b"));
        Assert.Equal(first.DataDirectory, upgraded.DataDirectory);
        Assert.Equal(first.Database, upgraded.Database);
        Assert.Equal(first.WorkspaceDirectory, upgraded.WorkspaceDirectory);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), first.DataDirectory);
        Assert.NotEqual(first.SourceDirectory, upgraded.SourceDirectory);
    }
    [Theory]
    [InlineData(".", null)]
    [InlineData("state", null)]
    [InlineData(null, "workspace")]
    public void Runtime_state_under_installed_binaries_is_rejected(string? data, string? workspace)
    {
        var binary = Path.Combine(Path.GetTempPath(), "installed-app");
        Assert.Throws<ArgumentException>(() => RuntimePaths.Resolve(binary,
            data is null ? null : Path.Combine(binary, data), workspace is null ? null : Path.Combine(binary, workspace)));
    }
    [Fact]
    public void Assembly_file_and_display_versions_agree()
    {
        var assembly = typeof(ApplicationVersion).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        Assert.Equal("Version " + version, ApplicationVersion.Display);
        var file = FileVersionInfo.GetVersionInfo(assembly.Location);
        Assert.StartsWith(version, file.FileVersion);
        Assert.StartsWith(version, file.ProductVersion);
    }
    [Fact]
    public async Task First_run_initializes_empty_then_explicit_demo_loading_is_idempotent()
    {
        using var env = new ConflictEnvironment();
        Directory.CreateDirectory(env.Root);
        var initializer = Initializer(env, Path.Combine(AppContext.BaseDirectory, "demo-data"));
        await initializer.InitializeAsync();
        await using (var db = env.Db())
        {
            Assert.Empty(await db.Documents.ToListAsync());
            Assert.Empty(await db.WorkflowEvents.ToListAsync());
            Assert.Equal(3, (await db.Database.GetAppliedMigrationsAsync()).Count());
        }
        var vm = new MainViewModel(env.Service(), new Dialogs(), NullLogger<MainViewModel>.Instance, initializer);
        await vm.RefreshAsync();
        Assert.True(vm.IsEmpty);
        Assert.True(vm.LoadDemoCommand.CanExecute(null));
        await vm.LoadDemoCommand.ExecuteAsync();
        Assert.Equal(5, vm.Documents.Count);
        Assert.False(vm.IsEmpty);
        Assert.False(vm.LoadDemoCommand.CanExecute(null));
        await initializer.InitializeAsync();
        await initializer.LoadDemoDataAsync();
        await using var read = env.Db();
        Assert.Equal(5, await read.Documents.CountAsync());
        Assert.Equal(5, await read.Versions.CountAsync());
        Assert.Equal(5, await read.WorkflowEvents.CountAsync());
    }
    [Fact]
    public async Task Incomplete_sample_package_does_not_create_partial_demo_metadata()
    {
        using var env = new ConflictEnvironment();
        Directory.CreateDirectory(env.SourceRoot);
        var initializer = Initializer(env, env.SourceRoot);
        await initializer.InitializeAsync();
        await Assert.ThrowsAsync<FileNotFoundException>(() => initializer.LoadDemoDataAsync());
        await using var db = env.Db();
        Assert.Empty(await db.Documents.ToListAsync());
        Assert.Empty(await db.Versions.ToListAsync());
        Assert.Empty(await db.WorkflowEvents.ToListAsync());
    }
    [Fact]
    public async Task Startup_over_existing_workflow_preserves_checkout_versions_audit_and_bytes()
    {
        using var env = new ConflictEnvironment();
        await env.InitializeAsync();
        await env.CreateConflictAsync();
        await env.Service().ListAsync();
        var before = await env.Service().GetHistoryAsync(env.Document.Id);
        var bytes = File.ReadAllBytes(env.WorkingPath);
        await Initializer(env, env.SourceRoot).InitializeAsync();
        var after = await env.Service().GetHistoryAsync(env.Document.Id);
        Assert.Equal(before.Context.WorkingCopy!.CheckedOutAt, after.Context.WorkingCopy!.CheckedOutAt);
        Assert.Equal(before.Context.Document.CurrentVersion, after.Context.Document.CurrentVersion);
        Assert.Equal(before.Versions.Select(x => x.Version.Id), after.Versions.Select(x => x.Version.Id));
        Assert.Equal(before.Events.Select(x => x.Id), after.Events.Select(x => x.Id));
        Assert.Equal(bytes, File.ReadAllBytes(env.WorkingPath));
    }
    private static DatabaseInitializer Initializer(ConflictEnvironment env, string source) => new(new Factory(env.Options),
        new DemoDocumentSource(source), new FileHashService(), NullLogger<DatabaseInitializer>.Instance);
    private sealed class Factory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    { public AppDbContext CreateDbContext() => new(options); }
    private sealed class Dialogs : IUserDialogService { public bool ConfirmDiscard(string name, bool edits) => false; }
}
