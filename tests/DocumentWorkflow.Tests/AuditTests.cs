using DocumentWorkflow.App;
using DocumentWorkflow.Application;
using DocumentWorkflow.Domain;
using DocumentWorkflow.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocumentWorkflow.Tests;

public sealed class AuditTests : IDisposable
{
    private readonly ConflictEnvironment env = new();
    [Fact]
    public async Task Duplicate_append_is_ignored_within_one_transaction_and_after_restart()
    {
        await env.InitializeAsync();
        var store = new WorkflowStore(new Factory(env.Options));
        await using (var session = await store.BeginAsync(env.Document.Id))
        {
            Assert.True(await session.AppendEventAsync(new(env.Document.Id, WorkflowEventType.RecoveryPerformed, "one-real-action")));
            Assert.False(await session.AppendEventAsync(new(env.Document.Id, WorkflowEventType.RecoveryPerformed, "one-real-action")));
            await session.CommitAsync();
        }
        await using (var session = await store.BeginAsync(env.Document.Id))
            Assert.False(await session.AppendEventAsync(new(env.Document.Id, WorkflowEventType.RecoveryPerformed, "one-real-action")));
        Assert.Single((await env.Service().GetHistoryAsync(env.Document.Id)).Events, x => x.Type == WorkflowEventType.RecoveryPerformed);
    }
    [Fact]
    public async Task Failed_metadata_commit_rolls_back_checkin_event_with_the_version()
    {
        await env.InitializeAsync();
        ConflictEnvironment.Edit(env.WorkingPath, "retained edits");
        var bytes = File.ReadAllBytes(env.WorkingPath);
        var failing = new FailingStore(new WorkflowStore(new Factory(env.Options)));
        await Assert.ThrowsAsync<WorkflowException>(() => env.Service(store: failing).CheckInAsync(env.Document.Id));
        var history = await env.Service().GetHistoryAsync(env.Document.Id);
        Assert.Single(history.Versions);
        Assert.Equal(WorkflowEventType.CheckoutCreated, Assert.Single(history.Events).Type);
        Assert.Equal(bytes, File.ReadAllBytes(env.WorkingPath));
    }
    [Fact]
    public async Task Unchanged_checkin_and_unconfirmed_modified_discard_do_not_append_events()
    {
        await env.InitializeAsync();
        await Assert.ThrowsAsync<WorkflowException>(() => env.Service().CheckInAsync(env.Document.Id));
        ConflictEnvironment.Edit(env.WorkingPath, "retained");
        await Assert.ThrowsAsync<WorkflowException>(() => env.Service().DiscardAsync(env.Document.Id));
        Assert.Equal(WorkflowEventType.CheckoutCreated, Assert.Single((await env.Service().GetHistoryAsync(env.Document.Id)).Events).Type);
    }
    [Fact]
    public async Task Checkout_and_checkin_append_once_with_timestamp_and_version_scope()
    {
        var earliest = DateTime.UtcNow;
        await env.InitializeAsync();
        var before = await env.Service().GetHistoryAsync(env.Document.Id);
        var checkout = Assert.Single(before.Events);
        Assert.Equal(WorkflowEventType.CheckoutCreated, checkout.Type);
        Assert.Equal(before.Context.WorkingCopy!.CheckedOutAt, checkout.CheckoutAt);
        Assert.Equal(checkout.CheckoutAt, checkout.OccurredAt);
        Assert.Equal(1, checkout.BaseVersion);
        ConflictEnvironment.Edit(env.WorkingPath, "checked in");
        await env.Service().CheckInAsync(env.Document.Id);
        var history = await env.Service().GetHistoryAsync(env.Document.Id);
        Assert.Equal(new[] { 2, 1 }, history.Versions.Select(x => x.Version.VersionNumber));
        var checkin = Assert.Single(history.Events, x => x.Type == WorkflowEventType.CheckInCompleted);
        Assert.Equal(2, checkin.VersionNumber);
        Assert.Equal(1, checkin.BaseVersion);
        Assert.Equal(checkout.CheckoutAt, checkin.CheckoutAt);
        Assert.Equal(history.Versions[0].Version.CreatedAt, checkin.OccurredAt);
        Assert.All(history.Events, x => { Assert.Equal(env.Document.Id, x.DocumentId); Assert.InRange(x.OccurredAt, earliest, DateTime.UtcNow); });
    }
    [Fact]
    public async Task Repeated_modified_refresh_and_activation_do_not_append_guessed_modification_events()
    {
        await env.InitializeAsync();
        ConflictEnvironment.Edit(env.WorkingPath, "edits");
        var vm = new MainViewModel(env.Service(), new Dialogs(false), NullLogger<MainViewModel>.Instance);
        await vm.RefreshAsync();
        for (var i = 0; i < 20; i++) { await vm.RefreshAsync(); await vm.OnActivatedAsync(); }
        var events = (await env.Service().GetHistoryAsync(env.Document.Id)).Events;
        Assert.Equal(WorkflowEventType.CheckoutCreated, Assert.Single(events).Type);
        Assert.True(Assert.Single(vm.Documents).IsModified);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Conflict_observation_is_deduplicated_across_refresh_restart_and_failed_checkin(bool recovery)
    {
        await env.InitializeAsync();
        await env.CreateConflictAsync();
        for (var i = 0; i < 10; i++) await env.Service(recovery).ListAsync();
        Assert.False((await env.Service(recovery).CheckInWithResultAsync(env.Document.Id)).Succeeded);
        Assert.False((await env.Service(recovery).CheckInWithResultAsync(env.Document.Id)).Succeeded);
        var history = await env.Service().GetHistoryAsync(env.Document.Id);
        Assert.Equal(2, history.Versions.Count);
        var conflict = Assert.Single(history.Events, x => x.Type == WorkflowEventType.ConflictDetected);
        Assert.Equal(1, conflict.BaseVersion);
        Assert.Equal(2, conflict.VersionNumber);
        Assert.Equal(history.Context.WorkingCopy!.CheckedOutAt, conflict.CheckoutAt);
        Assert.Single(history.Events, x => x.Type == WorkflowEventType.CompetingVersionPublished);
        Assert.DoesNotContain(history.Events, x => x.Type == WorkflowEventType.CheckInCompleted);
    }
    [Fact]
    public async Task A_new_current_version_can_produce_one_new_conflict_observation_for_the_same_checkout()
    {
        await env.InitializeAsync();
        await env.CreateConflictAsync();
        await env.Service().ListAsync();
        await env.PublishAsync();
        await env.Service().ListAsync();
        await env.Service().ListAsync();
        var history = await env.Service().GetHistoryAsync(env.Document.Id);
        Assert.Equal(new int?[] { 3, 2 }, history.Events.Where(x => x.Type == WorkflowEventType.ConflictDetected).Select(x => x.VersionNumber));
    }
    [Fact]
    public async Task Unchanged_stale_missing_and_unreadable_copies_do_not_fabricate_conflict_events()
    {
        await env.InitializeAsync();
        await env.PublishAsync();
        await env.Service().ListAsync(); // Stale, but unchanged.
        ConflictEnvironment.Edit(env.WorkingPath, "edits");
        using (var locked = new FileStream(env.WorkingPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await env.Service().ListAsync();
        File.Delete(env.WorkingPath);
        await env.Service().ListAsync();
        Assert.DoesNotContain((await env.Service().GetHistoryAsync(env.Document.Id)).Events, x => x.Type == WorkflowEventType.ConflictDetected);
    }
    [Theory]
    [InlineData(false, WorkflowEventType.CheckoutDiscarded)]
    [InlineData(true, WorkflowEventType.ConflictDiscarded)]
    public async Task Confirmed_discard_appends_correct_event_and_cancelled_discard_appends_nothing(bool conflict, WorkflowEventType expected)
    {
        await env.InitializeAsync();
        if (conflict) await env.CreateConflictAsync();
        var dialogs = new Dialogs(false);
        var vm = new MainViewModel(env.Service(), dialogs, NullLogger<MainViewModel>.Instance);
        await vm.RefreshAsync();
        var before = (await env.Service().GetHistoryAsync(env.Document.Id)).Events.Select(x => x.Id).ToArray();
        await Assert.Single(vm.Documents).DiscardCommand.ExecuteAsync();
        Assert.Equal(before, (await env.Service().GetHistoryAsync(env.Document.Id)).Events.Select(x => x.Id));
        Assert.True(File.Exists(env.WorkingPath));
        dialogs.Confirm = true;
        await Assert.Single(vm.Documents).DiscardCommand.ExecuteAsync();
        var history = await env.Service().GetHistoryAsync(env.Document.Id);
        Assert.Equal(expected, Assert.Single(history.Events, x => x.Type == expected).Type);
        Assert.Null(history.Context.WorkingCopy);
        Assert.Equal(conflict ? 2 : 1, history.Context.Document.CurrentVersion);
    }
    [Fact]
    public async Task Actual_staging_restore_is_audited_once_but_missing_artifact_warning_is_not_a_recovery_event()
    {
        await env.InitializeAsync();
        ConflictEnvironment.Edit(env.WorkingPath, "retained");
        var bytes = File.ReadAllBytes(env.WorkingPath);
        File.Move(env.WorkingPath, env.WorkingPath + ".discard");
        await env.Service().ListAsync();
        Assert.Equal(bytes, File.ReadAllBytes(env.WorkingPath));
        await env.PublishAsync();
        File.Delete(env.Versions.Resolve(env.Document.Id, 2, env.Document.FileName));
        for (var i = 0; i < 5; i++) await env.Service().ListAsync();
        var history = await env.Service().GetHistoryAsync(env.Document.Id);
        var recovered = Assert.Single(history.Events, x => x.Type == WorkflowEventType.RecoveryPerformed);
        Assert.Contains("Restored interrupted working-copy staging", recovered.Details);
        Assert.Equal(1, recovered.BaseVersion);
        Assert.Equal(history.Context.WorkingCopy!.CheckedOutAt, recovered.CheckoutAt);
    }
    [Fact]
    public async Task Actual_quarantine_is_audited_once_and_preserves_bytes()
    {
        await env.InitializeAsync();
        var hash = await new FileHashService().HashAsync(env.SourcePath);
        var artifact = env.Versions.Resolve(env.Document.Id, 2, env.Document.FileName);
        await env.Versions.CreateAsync(env.Document.Id, 2, env.Document.FileName, env.SourcePath, hash);
        await env.Service().ListAsync();
        await env.Service().ListAsync();
        Assert.False(File.Exists(artifact));
        var quarantine = Assert.Single(Directory.GetFiles(env.RecoveryRoot));
        Assert.Equal(File.ReadAllBytes(env.SourcePath), File.ReadAllBytes(quarantine));
        var recovered = Assert.Single((await env.Service().GetHistoryAsync(env.Document.Id)).Events, x => x.Type == WorkflowEventType.RecoveryPerformed);
        Assert.Contains(quarantine, recovered.Details);
    }
    [Fact]
    public async Task Events_are_exactly_document_scoped_and_sessions_reject_cross_document_events()
    {
        await env.InitializeAsync();
        var other = new DocumentRecord("Other", env.Document.FileName);
        await using (var db = env.Db())
        {
            db.Documents.Add(other);
            db.Versions.Add(new(other.Id, 1, await new FileHashService().HashAsync(env.SourcePath), "Initial"));
            await db.SaveChangesAsync();
        }
        await env.Service().CheckOutAsync(other.Id);
        Assert.All((await env.Service().GetHistoryAsync(env.Document.Id)).Events, x => Assert.Equal(env.Document.Id, x.DocumentId));
        Assert.All((await env.Service().GetHistoryAsync(other.Id)).Events, x => Assert.Equal(other.Id, x.DocumentId));
        var store = new WorkflowStore(new Factory(env.Options));
        await using var session = await store.BeginAsync(env.Document.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.AppendEventAsync(new(other.Id, WorkflowEventType.DocumentCreated, "cross-document")));
    }
    [Fact]
    public async Task Audit_rows_cannot_be_updated_or_deleted_through_EF_or_SQL()
    {
        await env.InitializeAsync();
        await using (var db = env.Db())
        {
            var value = await db.WorkflowEvents.SingleAsync();
            db.Entry(value).Property(x => x.Details).CurrentValue = "rewrite";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var db = env.Db())
        {
            db.WorkflowEvents.Remove(await db.WorkflowEvents.SingleAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var db = env.Db())
        {
            await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE WorkflowEvents SET Details = 'rewrite'"));
            await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM WorkflowEvents"));
        }
        Assert.Single((await env.Service().GetHistoryAsync(env.Document.Id)).Events);
    }
    [Fact]
    public async Task Additive_migration_preserves_legacy_versions_and_checkout_without_backfilling_events()
    {
        Directory.CreateDirectory(env.SourceRoot);
        await using var db = env.Db();
        var previous = db.Database.GetMigrations().Last(x => !x.EndsWith("WorkflowEvents"));
        await db.GetService<IMigrator>().MigrateAsync(previous);
        db.Documents.Add(env.Document);
        var version = new DocumentVersion(env.Document.Id, 1, "old hash", "old note");
        var copy = new WorkingCopy(env.Document.Id, env.WorkingPath, 1, "old hash");
        db.Versions.Add(version);
        db.WorkingCopies.Add(copy);
        env.Document.CheckOut();
        await db.SaveChangesAsync();
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(version.Id, (await db.Versions.SingleAsync()).Id);
        Assert.Equal("old note", (await db.Versions.SingleAsync()).ChangeNote);
        Assert.Equal(copy.CheckedOutAt, (await db.WorkingCopies.SingleAsync()).CheckedOutAt);
        Assert.Empty(await db.WorkflowEvents.ToListAsync());
    }
    [Fact]
    public async Task Fresh_database_creation_is_audited_once_with_no_startup_duplicates()
    {
        Directory.CreateDirectory(env.SourceRoot);
        var initializer = new DatabaseInitializer(new Factory(env.Options), new DemoDocumentSource(env.SourceRoot),
            new FileHashService(), NullLogger<DatabaseInitializer>.Instance);
        await initializer.InitializeAsync(seedDemo: true);
        await initializer.InitializeAsync(seedDemo: true);
        await using var db = env.Db();
        var events = await db.WorkflowEvents.ToListAsync();
        Assert.Equal(5, events.Count);
        Assert.All(events, x => { Assert.Equal(WorkflowEventType.DocumentCreated, x.Type); Assert.Equal(1, x.VersionNumber); });
        Assert.Equal(5, events.Select(x => x.DocumentId).Distinct().Count());
    }
    [Fact]
    public async Task Timeline_labels_are_friendly_and_legacy_empty_state_is_explicit()
    {
        await env.InitializeAsync();
        var history = await env.Service().GetHistoryAsync(env.Document.Id);
        Assert.Equal("Checked out from v1", new WorkflowEventViewModel(Assert.Single(history.Events)).Label);
        Assert.Equal("Recovery performed", new WorkflowEventViewModel(new(env.Document.Id, WorkflowEventType.RecoveryPerformed, "recovery-test")).Label);
        Assert.Equal("Document created", new WorkflowEventViewModel(new(env.Document.Id, WorkflowEventType.DocumentCreated, "creation-test")).Label);
        var vm = new HistoryViewModel(env.Document.Id, env.Service());
        Assert.Contains("No workflow events", vm.EmptyEvents);
        Assert.Contains("Earlier events are not reconstructed", vm.AuditNotice);
        await vm.RefreshAsync();
        Assert.Empty(vm.EmptyEvents);
    }
    private sealed class Dialogs(bool confirm) : IUserDialogService
    {
        public bool Confirm { get; set; } = confirm;
        public bool ConfirmDiscard(string name, bool edits) => Confirm;
    }
    private sealed class Factory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    { public AppDbContext CreateDbContext() => new(options); }
    private sealed class FailingStore(IWorkflowStore inner) : IWorkflowStore
    {
        public Task<IReadOnlyList<DocumentSnapshot>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
        public async Task<IWorkflowSession> BeginAsync(Guid id, CancellationToken cancellationToken = default) => new Session(await inner.BeginAsync(id, cancellationToken));
        private sealed class Session(IWorkflowSession inner) : IWorkflowSession
        {
            public DocumentRecord Document => inner.Document;
            public WorkingCopy? WorkingCopy => inner.WorkingCopy;
            public IReadOnlyList<DocumentVersion> Versions => inner.Versions;
            public void Add(WorkingCopy copy) => inner.Add(copy);
            public void AddVersion(DocumentVersion version) => inner.AddVersion(version);
            public void RemoveWorkingCopy() => inner.RemoveWorkingCopy();
            public Task<bool> AppendEventAsync(WorkflowEvent value, CancellationToken cancellationToken = default) => inner.AppendEventAsync(value, cancellationToken);
            public Task CommitAsync(CancellationToken cancellationToken = default) => throw new IOException("Simulated metadata failure");
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
    public void Dispose() => env.Dispose();
}
