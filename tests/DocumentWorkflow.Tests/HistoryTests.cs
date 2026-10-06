using DocumentWorkflow.App;
using DocumentWorkflow.Application;
using DocumentWorkflow.Domain;
using DocumentWorkflow.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocumentWorkflow.Tests;

public sealed class HistoryTests : IDisposable
{
    private readonly ConflictEnvironment env = new();
    [Fact]
    public async Task History_window_constructs_and_lays_out_on_a_WPF_thread()
    {
        await env.InitializeAsync();
        var vm = new HistoryViewModel(env.Document.Id, env.Service());
        await vm.RefreshAsync();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new HistoryWindow(vm);
                var content = Assert.IsAssignableFrom<System.Windows.FrameworkElement>(window.Content);
                content.Measure(new System.Windows.Size(1050, 790));
                content.Arrange(new System.Windows.Rect(0, 0, 1050, 790));
                Assert.True(content.ActualHeight > 0);
                window.Close();
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }
    private async Task CreateThreeVersionsAsync()
    {
        await env.InitializeAsync();
        ConflictEnvironment.Edit(env.WorkingPath, "v2 edits");
        Assert.True((await env.Service().CheckInWithResultAsync(env.Document.Id)).Succeeded);
        await env.PublishAsync();
    }
    [Fact]
    public async Task History_returns_all_versions_newest_first_and_exactly_one_current_after_restart()
    {
        await CreateThreeVersionsAsync();
        var history = await env.Service().GetHistoryAsync(env.Document.Id);
        Assert.Equal(new[] { 3, 2, 1 }, history.Versions.Select(x => x.Version.VersionNumber));
        Assert.Equal(3, Assert.Single(history.Versions, x => x.IsCurrent).Version.VersionNumber);
        Assert.Equal(1, history.Versions[1].Version.BaseVersion);
        Assert.Equal(2, history.Versions[0].Version.BaseVersion);
        var restarted = await env.Service().GetHistoryAsync(env.Document.Id);
        Assert.Equal(history.Versions.Select(x => x.Version.Id), restarted.Versions.Select(x => x.Version.Id));
        Assert.Equal(history.Events.Select(x => x.Id), restarted.Events.Select(x => x.Id));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Historical_open_preserves_metadata_artifacts_and_optional_active_checkout(bool active)
    {
        await CreateThreeVersionsAsync();
        if (active) { await env.Service().CheckOutAsync(env.Document.Id); ConflictEnvironment.Edit(env.WorkingPath, "retained edits"); }
        var before = await env.Service().GetHistoryAsync(env.Document.Id);
        var artifactBytes = before.Versions.Select(x => File.ReadAllBytes(x.ArtifactPath)).ToArray();
        var localBytes = active ? File.ReadAllBytes(env.WorkingPath) : null;
        await env.Service().OpenVersionAsync(env.Document.Id, 2);
        var inspection = env.Opener.Path!;
        Assert.Contains(Path.Combine(".inspection", env.Document.Id.ToString("N"), "v2"), inspection);
        Assert.DoesNotContain(inspection, before.Versions.Select(x => x.ArtifactPath));
        Assert.NotEqual(env.WorkingPath, inspection);
        Assert.Equal(artifactBytes[1], File.ReadAllBytes(inspection));
        File.SetAttributes(inspection, FileAttributes.Normal);
        ConflictEnvironment.Edit(inspection, "inspection-only edits");
        var after = await env.Service().GetHistoryAsync(env.Document.Id);
        Assert.Equal(3, after.Context.Document.CurrentVersion);
        Assert.Equal(before.Context.Document.UpdatedAt, after.Context.Document.UpdatedAt);
        Assert.Equal(before.Versions.Select(x => x.Version.Id), after.Versions.Select(x => x.Version.Id));
        Assert.Equal(before.Events.Select(x => x.Id), after.Events.Select(x => x.Id));
        for (var i = 0; i < 3; i++) Assert.Equal(artifactBytes[i], File.ReadAllBytes(after.Versions[i].ArtifactPath));
        if (active)
        {
            Assert.Equal(localBytes, File.ReadAllBytes(env.WorkingPath));
            Assert.Equal(before.Context.WorkingCopy!.CheckedOutAt, after.Context.WorkingCopy!.CheckedOutAt);
            Assert.Equal(WorkingCopyState.Modified, after.Context.Evaluation!.State);
        }
        else Assert.Null(after.Context.WorkingCopy);
    }
    [Fact]
    public async Task Initial_version_open_uses_a_verified_inspection_copy()
    {
        await env.InitializeAsync();
        await env.Service().OpenVersionAsync(env.Document.Id, 1);
        Assert.Equal(File.ReadAllBytes(env.SourcePath), File.ReadAllBytes(env.Opener.Path!));
        Assert.NotEqual(env.SourcePath, env.Opener.Path);
        Assert.Equal(1, (await env.Service().GetHistoryAsync(env.Document.Id)).Context.Document.CurrentVersion);
    }
    [Fact]
    public async Task Unknown_or_tampered_history_is_never_opened()
    {
        await CreateThreeVersionsAsync();
        await Assert.ThrowsAsync<WorkflowException>(() => env.Service().OpenVersionAsync(env.Document.Id, 99));
        var artifact = env.Versions.Resolve(env.Document.Id, 2, env.Document.FileName);
        ConflictEnvironment.Edit(artifact, "tampered");
        await Assert.ThrowsAsync<WorkflowException>(() => env.Service().OpenVersionAsync(env.Document.Id, 2));
        Assert.Null(env.Opener.Path);
        Assert.Equal(3, (await env.Service().GetHistoryAsync(env.Document.Id)).Context.Document.CurrentVersion);
    }
    [Fact]
    public async Task History_projects_stale_base_current_and_verified_conflict_without_mutating_checkout()
    {
        await env.InitializeAsync();
        await env.CreateConflictAsync();
        var history = await env.Service().GetHistoryAsync(env.Document.Id);
        Assert.Equal(1, history.Context.WorkingCopy!.BaseVersion);
        Assert.Equal(2, history.Context.Document.CurrentVersion);
        Assert.Equal(WorkingCopyState.Conflict, history.Context.Evaluation!.State);
        Assert.Equal(new[] { 2, 1 }, history.Versions.Select(x => x.Version.VersionNumber));
    }
    [Fact]
    public async Task History_view_model_has_current_badge_compact_hash_copyable_details_and_honest_origins()
    {
        await CreateThreeVersionsAsync();
        var vm = new HistoryViewModel(env.Document.Id, env.Service());
        await vm.RefreshAsync();
        Assert.Equal(new[] { 3, 2, 1 }, vm.Versions.Select(x => x.Number));
        Assert.Equal("Current", vm.Versions[0].CurrentBadge);
        Assert.All(vm.Versions.Skip(1), x => Assert.Empty(x.CurrentBadge));
        Assert.Equal(17, vm.SelectedVersion!.CompactHash.Length);
        Assert.Equal("Competing writer", vm.Versions[0].Origin);
        Assert.Equal("Check-in", vm.Versions[1].Origin);
        Assert.Equal("Not recorded", vm.Versions[2].Origin); // The fixture's old arbitrary note proves no origin type.
        Assert.Contains("SHA-256:", vm.VersionDetails);
        Assert.Contains(env.Document.Id.ToString(), vm.VersionDetails);
        Assert.Contains("Version ID:", vm.VersionDetails);
        vm.SelectedVersion = vm.Versions[2];
        await vm.OpenVersionCommand.ExecuteAsync();
        Assert.Contains(Path.Combine(".inspection", env.Document.Id.ToString("N"), "v1"), env.Opener.Path!);
    }
    [Fact]
    public async Task Main_details_open_history_and_refresh_it_immediately_after_discard()
    {
        await env.InitializeAsync();
        await env.CreateConflictAsync();
        var vm = new MainViewModel(env.Service(), new ConfirmingDialogs(), NullLogger<MainViewModel>.Instance);
        await vm.RefreshAsync();
        Assert.Single(vm.Documents).ViewDetailsCommand.Execute(null);
        HistoryViewModel? presented = null;
        vm.HistoryRequested += history => presented = history;
        await vm.HistoryCommand.ExecuteAsync();
        Assert.Same(presented, vm.ActiveHistory);
        Assert.True(presented!.IsConflict);
        Assert.Equal("Your checkout: based on v1", presented.CheckoutBase);
        Assert.Equal("Current: v2", presented.CurrentVersion);
        Assert.Contains(presented.Events, x => x.Label == "Conflict detected: base v1, current v2");
        await Assert.Single(vm.Documents).DiscardCommand.ExecuteAsync();
        Assert.Equal("No active checkout", presented.CheckoutBase);
        Assert.False(presented.IsConflict);
        Assert.Contains(presented.Events, x => x.Label == "Conflicting local checkout discarded");
        vm.CloseHistory(presented);
        Assert.Null(vm.ActiveHistory);
    }
    private sealed class ConfirmingDialogs : IUserDialogService { public bool ConfirmDiscard(string name, bool edits) => true; }
    public void Dispose() => env.Dispose();
}
