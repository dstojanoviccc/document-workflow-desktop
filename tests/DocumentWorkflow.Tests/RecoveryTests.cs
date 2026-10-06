using DocumentWorkflow.Application;
using DocumentWorkflow.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocumentWorkflow.Tests;

public sealed class RecoveryTests : IDisposable
{
    private readonly ConflictEnvironment env = new();
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Interrupted_precommit_check_in_restores_edits_and_quarantines_unpublished_artifact(bool staged)
    {
        await env.InitializeAsync();
        ConflictEnvironment.Edit(env.WorkingPath, "saved edits before crash");
        var edited = await File.ReadAllBytesAsync(env.WorkingPath);
        var hash = await new DocumentWorkflow.Infrastructure.FileHashService().HashAsync(env.WorkingPath);
        await env.Versions.CreateAsync(env.Document.Id, 2, env.Document.FileName, env.WorkingPath, hash);
        if (staged) File.Move(env.WorkingPath, env.WorkingPath + ".discard");
        await env.Service().ReconcileAsync();
        Assert.Equal(edited, await File.ReadAllBytesAsync(env.WorkingPath));
        Assert.False(File.Exists(env.WorkingPath + ".discard"));
        Assert.False(File.Exists(env.Versions.Resolve(env.Document.Id, 2, env.Document.FileName)));
        Assert.Equal(edited, await File.ReadAllBytesAsync(Assert.Single(Directory.EnumerateFiles(env.RecoveryRoot))));
        var snapshot = Assert.Single(await env.Service().ListAsync());
        Assert.Equal(1, snapshot.Document.CurrentVersion);
        Assert.Equal(WorkingCopyState.Modified, snapshot.Evaluation!.State);
        Assert.NotNull(snapshot.WorkingCopy);
        Assert.Null(await env.Service().CheckInAsync(env.Document.Id));
        Assert.Equal(2, Assert.Single(await env.Service().ListAsync()).Document.CurrentVersion);
    }
    [Fact]
    public async Task Commit_before_refresh_keeps_published_version_and_quarantines_leftover_staging()
    {
        await env.InitializeAsync();
        ConflictEnvironment.Edit(env.WorkingPath, "committed edits");
        var edited = await File.ReadAllBytesAsync(env.WorkingPath);
        Assert.Null(await env.Service().CheckInAsync(env.Document.Id));
        // Simulate durable metadata with filesystem cleanup/UI refresh interrupted.
        Directory.CreateDirectory(Path.GetDirectoryName(env.WorkingPath)!);
        await File.WriteAllBytesAsync(env.WorkingPath + ".discard", edited);
        await env.Service().ReconcileAsync();
        var snapshot = Assert.Single(await env.Service().ListAsync());
        Assert.Equal(2, snapshot.Document.CurrentVersion);
        Assert.Null(snapshot.WorkingCopy);
        Assert.Equal(WorkingCopyState.Available, snapshot.Document.Status);
        Assert.Equal(edited, await File.ReadAllBytesAsync(env.Versions.Resolve(env.Document.Id, 2, env.Document.FileName)));
        Assert.False(File.Exists(env.WorkingPath));
        Assert.False(File.Exists(env.WorkingPath + ".discard"));
        await using var db = env.Db();
        Assert.Equal(2, await db.Versions.CountAsync());
        await Assert.ThrowsAsync<WorkflowException>(() => env.Service().CheckInAsync(env.Document.Id));
    }
    [Fact]
    public async Task Recovery_keeps_referenced_versions_active_edits_and_unknown_files_byte_identical()
    {
        await env.InitializeAsync();
        await env.CreateConflictAsync();
        var edits = await File.ReadAllBytesAsync(env.WorkingPath);
        var publishedPath = env.Versions.Resolve(env.Document.Id, 2, env.Document.FileName);
        var published = await File.ReadAllBytesAsync(publishedPath);
        var orphan = env.Versions.Resolve(env.Document.Id, 3, env.Document.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(orphan)!);
        await File.WriteAllTextAsync(orphan, "interrupted internal content");
        var unknown = Path.Combine(Path.GetDirectoryName(orphan)!, "unknown-user-file.txt");
        await File.WriteAllTextAsync(unknown, "unknown content");
        await File.WriteAllTextAsync(env.WorkingPath + ".discard", "extra staging bytes");
        await env.Service().ReconcileAsync();
        Assert.Equal(edits, await File.ReadAllBytesAsync(env.WorkingPath));
        Assert.Equal(published, await File.ReadAllBytesAsync(publishedPath));
        Assert.Equal("unknown content", await File.ReadAllTextAsync(unknown));
        Assert.False(File.Exists(orphan));
        Assert.Equal(2, Directory.EnumerateFiles(env.RecoveryRoot).Count());
        await env.Service().ReconcileAsync();
        Assert.Equal(2, Directory.EnumerateFiles(env.RecoveryRoot).Count());
        Assert.Equal(WorkingCopyState.Conflict, Assert.Single(await env.Service().ListAsync()).Evaluation!.State);
    }
    [Fact]
    public async Task Locked_staging_reports_warning_and_recovers_on_retry_without_touching_active_edits()
    {
        await env.InitializeAsync();
        var edits = await File.ReadAllBytesAsync(env.WorkingPath);
        var staged = env.WorkingPath + ".discard";
        await File.WriteAllTextAsync(staged, "staging bytes");
        using (var locked = new FileStream(staged, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var snapshot = Assert.Single(await env.Service().ListAsync());
            Assert.Contains("Recovery could not inspect", snapshot.WorkspaceWarning);
            Assert.True(File.Exists(staged));
        }
        await env.Service().ReconcileAsync();
        Assert.False(File.Exists(staged));
        Assert.Equal(edits, await File.ReadAllBytesAsync(env.WorkingPath));
        Assert.Equal("staging bytes", await File.ReadAllTextAsync(Assert.Single(Directory.EnumerateFiles(env.RecoveryRoot))));
    }
    [Fact]
    public async Task Recovery_serializes_with_active_check_in_and_never_quarantines_a_valid_commit()
    {
        await env.InitializeAsync();
        ConflictEnvironment.Edit(env.WorkingPath, "racing operation");
        await Task.WhenAll(env.Service().CheckInAsync(env.Document.Id), env.Service().ReconcileAsync());
        var snapshot = Assert.Single(await env.Service().ListAsync());
        Assert.Equal(2, snapshot.Document.CurrentVersion);
        Assert.Null(snapshot.WorkingCopy);
        Assert.True(File.Exists(env.Versions.Resolve(env.Document.Id, 2, env.Document.FileName)));
        Assert.False(Directory.Exists(env.RecoveryRoot));
    }
    public void Dispose() => env.Dispose();
}
