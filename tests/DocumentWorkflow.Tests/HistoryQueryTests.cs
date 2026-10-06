using System.Data.Common;
using DocumentWorkflow.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DocumentWorkflow.Tests;

public sealed class HistoryQueryTests
{
    [Fact]
    public async Task History_uses_three_bounded_queries_regardless_of_version_count()
    {
        using var env = new ConflictEnvironment();
        await env.InitializeAsync();
        await env.PublishAsync();
        await env.PublishAsync();
        var counter = new QueryCounter();
        var options = new DbContextOptionsBuilder<AppDbContext>(env.Options).AddInterceptors(counter).Options;
        var history = await new WorkflowStore(new Factory(options)).GetHistoryAsync(env.Document.Id);
        Assert.Equal(3, history.Versions.Count);
        Assert.Equal(3, counter.Reads);
        Assert.Equal(new[] { 3, 2, 1 }, history.Versions.Select(x => x.VersionNumber));
    }
    private sealed class Factory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    { public AppDbContext CreateDbContext() => new(options); }
    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Reads { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Reads++; return ValueTask.FromResult(result); }
    }
}
