using DocumentWorkflow.App;
using DocumentWorkflow.Application;
using DocumentWorkflow.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocumentWorkflow.Tests;

public class ActivationTests
{
    [Fact]
    public async Task Activation_refreshes_state_but_initial_window_activation_does_not_duplicate_startup_load()
    {
        var workflow = new ActivationWorkflow();
        var vm = new MainViewModel(workflow, new NoDialogs(), NullLogger<MainViewModel>.Instance);
        await vm.OnActivatedAsync();
        Assert.Equal(0, workflow.Reads);
        await vm.RefreshAsync();
        Assert.Equal("Unchanged", Assert.Single(vm.Documents).Status);
        workflow.State = WorkingCopyState.Modified;
        await vm.OnActivatedAsync();
        Assert.Equal("Modified", Assert.Single(vm.Documents).Status);
        Assert.Equal(2, workflow.Reads);
    }
    [Fact]
    public async Task Activation_during_a_read_is_coalesced_and_processed_after_that_read()
    {
        var workflow = new ActivationWorkflow();
        var vm = new MainViewModel(workflow, new NoDialogs(), NullLogger<MainViewModel>.Instance);
        await vm.RefreshAsync();
        workflow.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = vm.RefreshAsync();
        Assert.True(vm.IsBusy);
        await vm.OnActivatedAsync();
        await vm.OnActivatedAsync();
        workflow.State = WorkingCopyState.Modified;
        workflow.Pending.SetResult();
        await refresh;
        Assert.Equal(3, workflow.Reads);
        Assert.Equal(1, workflow.MaxConcurrentReads);
        Assert.Equal("Modified", Assert.Single(vm.Documents).Status);
        Assert.False(vm.IsBusy);
    }
    private sealed class NoDialogs : IUserDialogService { public bool ConfirmDiscard(string name, bool localEditsMayBeLost) => false; }
    private sealed class ActivationWorkflow : IDocumentWorkflowService
    {
        private readonly DocumentRecord document = new("Demo", "demo.txt");
        private int active;
        public int Reads { get; private set; }
        public int MaxConcurrentReads { get; private set; }
        public WorkingCopyState State { get; set; } = WorkingCopyState.Unchanged;
        public TaskCompletionSource? Pending { get; set; }
        public async Task<IReadOnlyList<DocumentSnapshot>> ListAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            MaxConcurrentReads = Math.Max(MaxConcurrentReads, ++active);
            try
            {
                var wait = Pending;
                if (wait is not null) { await wait.Task; Pending = null; }
                return [new(document, new WorkingCopy(document.Id, "isolated/demo.txt", 1, "BASE"), null,
                    new WorkingCopyEvaluation(State, State == WorkingCopyState.Modified ? "CHANGED" : "BASE"))];
            }
            finally { active--; }
        }
        public Task CheckOutAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task OpenAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> DiscardAsync(Guid id, CancellationToken cancellationToken = default, bool allowModified = false) => Task.FromResult<string?>(null);
        public Task ReconcileAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
