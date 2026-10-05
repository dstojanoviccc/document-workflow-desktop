using DocumentWorkflow.App;
using DocumentWorkflow.Application;
using DocumentWorkflow.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocumentWorkflow.Tests;

public class ViewModelTests
{
    [Fact]
    public async Task Refresh_replaces_rows_and_details_commands_select_and_close()
    {
        var repository = new StubRepository();
        var vm = new MainViewModel(repository, NullLogger<MainViewModel>.Instance);
        await vm.RefreshAsync();
        await vm.RefreshAsync();
        var row = Assert.Single(vm.Documents);
        Assert.Equal("1 documents", vm.CountText);
        Assert.False(vm.IsBusy);
        row.ViewDetailsCommand.Execute(null);
        Assert.Same(row, vm.SelectedDocument);
        Assert.True(vm.DetailsVisible);
        vm.CloseDetailsCommand.Execute(null);
        Assert.False(vm.DetailsVisible);
    }

    [Fact]
    public async Task Failed_refresh_preserves_rows_and_allows_retry()
    {
        var repository = new StubRepository();
        var vm = new MainViewModel(repository, NullLogger<MainViewModel>.Instance);
        await vm.RefreshAsync();
        repository.Fail = true;
        await vm.RefreshAsync();
        Assert.Single(vm.Documents);
        Assert.Contains("Could not load", vm.Message);
        Assert.False(vm.IsBusy);
        repository.Fail = false;
        await vm.RefreshAsync();
        Assert.Contains("Loaded from SQLite", vm.Message);
    }

    [Fact]
    public async Task Empty_library_has_an_explicit_status()
    {
        var vm = new MainViewModel(new StubRepository { Empty = true }, NullLogger<MainViewModel>.Instance);
        await vm.RefreshAsync();
        Assert.Empty(vm.Documents);
        Assert.Equal("No documents found.", vm.Message);
    }

    private sealed class StubRepository : IDocumentRepository
    {
        public bool Fail { get; set; }
        public bool Empty { get; set; }
        public Task<IReadOnlyList<DocumentRecord>> ListAsync(CancellationToken cancellationToken = default)
        {
            if (Fail) throw new IOException("Simulated database read failure.");
            return Task.FromResult<IReadOnlyList<DocumentRecord>>(Empty ? [] : [new("Guide", "Guide.pdf")]);
        }
    }
}
