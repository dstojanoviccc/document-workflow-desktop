using DocumentWorkflow.Application;
using DocumentWorkflow.Domain;
using DocumentWorkflow.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocumentWorkflow.Tests;

public sealed class WorkingCopyStateTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "workflow-state-" + Guid.NewGuid().ToString("N"));
    private readonly DocumentRecord document = new("Demo", "demo.bin");
    private LocalWorkspaceService Workspace => new(Path.Combine(root, "workspace"), Path.Combine(root, "source"));
    private WorkingCopyStateService Evaluator => new(Workspace, new FileHashService(), NullLogger.Instance);
    private async Task<WorkingCopy> CreateAsync()
    {
        var path = Workspace.Resolve(document.Id, document.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, [0, 1, 2, 3, 255]);
        return new(document.Id, path, 1, await new FileHashService().HashAsync(path));
    }
    [Fact]
    public async Task Content_change_and_exact_restoration_change_derived_state()
    {
        var copy = await CreateAsync();
        var evaluator = Evaluator;
        var original = await File.ReadAllBytesAsync(copy.LocalPath);
        Assert.Equal(WorkingCopyState.Unchanged, (await evaluator.EvaluateAsync(document, copy)).State);
        File.SetLastWriteTimeUtc(copy.LocalPath, DateTime.UtcNow.AddDays(-10));
        Assert.Equal(WorkingCopyState.Unchanged, (await evaluator.EvaluateAsync(document, copy)).State);
        await File.WriteAllBytesAsync(copy.LocalPath, [0, 9, 2, 3, 255]);
        var modified = await evaluator.EvaluateAsync(document, copy);
        Assert.Equal(WorkingCopyState.Modified, modified.State);
        Assert.NotEqual(copy.LastKnownHash, modified.CurrentHash);
        await File.WriteAllBytesAsync(copy.LocalPath, original);
        Assert.Equal(WorkingCopyState.Unchanged, (await evaluator.EvaluateAsync(document, copy)).State);
        Assert.Equal(copy.LastKnownHash, (await evaluator.EvaluateAsync(document, copy)).CurrentHash);
    }
    [Fact]
    public async Task Missing_file_is_not_modified_and_is_never_recreated()
    {
        var copy = await CreateAsync();
        File.Delete(copy.LocalPath);
        var result = await Evaluator.EvaluateAsync(document, copy);
        Assert.Null(result.State);
        Assert.Null(result.CurrentHash);
        Assert.Equal(EvaluationIssue.Missing, result.Issue);
        Assert.False(File.Exists(copy.LocalPath));
    }
    [Fact]
    public async Task Locked_file_keeps_last_reliable_state_without_claiming_a_current_hash()
    {
        var copy = await CreateAsync();
        var evaluator = Evaluator;
        await File.WriteAllBytesAsync(copy.LocalPath, [7, 8, 9]);
        Assert.Equal(WorkingCopyState.Modified, (await evaluator.EvaluateAsync(document, copy)).State);
        using (var locked = new FileStream(copy.LocalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = await evaluator.EvaluateAsync(document, copy);
            Assert.Equal(WorkingCopyState.Modified, result.State);
            Assert.Null(result.CurrentHash);
            Assert.Equal(EvaluationIssue.Unreadable, result.Issue);
            Assert.Null((await Evaluator.EvaluateAsync(document, copy)).State);
        }
        Assert.Equal(EvaluationIssue.None, (await evaluator.EvaluateAsync(document, copy)).Issue);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
