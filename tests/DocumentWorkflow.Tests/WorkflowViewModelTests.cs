using DocumentWorkflow.App;
using DocumentWorkflow.Application;
using DocumentWorkflow.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocumentWorkflow.Tests;

public class WorkflowViewModelTests
{
    [Fact]
    public async Task Checkout_and_discard_update_rows_without_manual_refresh()
    {
        var service = new StubWorkflow();
        var dialogs = new Dialogs { Confirm = true };
        var vm = new MainViewModel(service, dialogs, NullLogger<MainViewModel>.Instance);
        await vm.RefreshAsync();
        var available = Assert.Single(vm.Documents);
        Assert.True(available.CanCheckOut);
        available.CheckOutCommand.Execute(null);
        var checkedOut = Assert.Single(vm.Documents);
        Assert.True(checkedOut.HasWorkingCopy);
        Assert.True(checkedOut.CanOpen);
        Assert.False(checkedOut.CanCheckOut);
        Assert.Equal("Unchanged", checkedOut.Status);
        checkedOut.ViewDetailsCommand.Execute(null);
        checkedOut.OpenCommand.Execute(null);
        Assert.Equal(1, service.OpenCount);
        Assert.True(vm.DetailsVisible);
        Assert.Equal("v1", vm.SelectedDocument!.BaseVersion);
        Assert.NotEmpty(vm.SelectedDocument.LocalPath);
        Assert.NotEmpty(vm.SelectedDocument.CheckedOutAt);
        Assert.NotEmpty(vm.SelectedDocument.BaseHash);
        Assert.Single(vm.Documents).DiscardCommand.Execute(null);
        Assert.Equal(1, dialogs.Count);
        Assert.True(Assert.Single(vm.Documents).CanCheckOut);
        Assert.False(vm.SelectedDocument.HasWorkingCopy);
        Assert.False(vm.IsBusy);
    }
    [Fact]
    public async Task Cancelled_discard_does_not_call_service()
    {
        var service = new StubWorkflow();
        await service.CheckOutAsync(service.Document.Id);
        var dialogs = new Dialogs();
        var vm = new MainViewModel(service, dialogs, NullLogger<MainViewModel>.Instance);
        await vm.RefreshAsync();
        Assert.Single(vm.Documents).DiscardCommand.Execute(null);
        Assert.Equal(1, dialogs.Count);
        Assert.Equal(0, service.DiscardCount);
        Assert.True(Assert.Single(vm.Documents).HasWorkingCopy);
    }
    [Fact]
    public async Task Missing_copy_disables_open_retains_discard_and_explains_problem()
    {
        var service = new StubWorkflow { Warning = "The local file is missing." };
        await service.CheckOutAsync(service.Document.Id);
        var vm = new MainViewModel(service, new Dialogs(), NullLogger<MainViewModel>.Instance);
        await vm.RefreshAsync();
        var row = Assert.Single(vm.Documents);
        Assert.False(row.CanOpen);
        Assert.False(row.CanCheckOut);
        Assert.True(row.HasWorkingCopy);
        Assert.True(row.HasWarning);
        Assert.Contains("attention", vm.Message);
        row.ViewDetailsCommand.Execute(null);
        Assert.Contains("missing", vm.SelectedDocument!.Warning);
    }
    [Fact]
    public async Task Workflow_error_is_visible_and_commands_remain_usable()
    {
        var service = new StubWorkflow { Fail = true };
        var vm = new MainViewModel(service, new Dialogs(), NullLogger<MainViewModel>.Instance);
        await vm.RefreshAsync();
        Assert.Single(vm.Documents).CheckOutCommand.Execute(null);
        Assert.Equal("The demo source file is missing.", vm.Message);
        Assert.False(vm.IsBusy);
        Assert.True(Assert.Single(vm.Documents).CanCheckOut);
    }
    private sealed class Dialogs : IUserDialogService
    {
        public bool Confirm { get; set; }
        public int Count { get; private set; }
        public bool ConfirmDiscard(string name, bool localEditsMayBeLost) { Count++; return Confirm; }
    }
    private sealed class StubWorkflow : IDocumentWorkflowService
    {
        public DocumentRecord Document { get; } = new("Demo", "demo.txt");
        private WorkingCopy? copy;
        public bool Fail { get; set; }
        public string? Warning { get; set; }
        public int OpenCount { get; private set; }
        public int DiscardCount { get; private set; }
        public Task<IReadOnlyList<DocumentSnapshot>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DocumentSnapshot>>([new(Document, copy, Warning,
                copy is null ? null : new WorkingCopyEvaluation(WorkingCopyState.Unchanged, copy.LastKnownHash))]);
        public Task<string?> CheckInAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task CheckOutAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new WorkflowException("The demo source file is missing.");
            Document.CheckOut();
            copy = new WorkingCopy(id, "isolated-workspace/demo.txt", Document.CurrentVersion, "basehash");
            return Task.CompletedTask;
        }
        public Task OpenAsync(Guid id, CancellationToken cancellationToken = default) { OpenCount++; return Task.CompletedTask; }
        public Task<string?> DiscardAsync(Guid id, CancellationToken cancellationToken = default, bool allowModified = false)
        {
            DiscardCount++;
            copy = null;
            Document.DiscardCheckout();
            return Task.FromResult<string?>(null);
        }
        public Task ReconcileAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
