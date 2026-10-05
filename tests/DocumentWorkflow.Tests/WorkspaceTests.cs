using DocumentWorkflow.Infrastructure;

namespace DocumentWorkflow.Tests;

public sealed class WorkspaceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "workflow-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Guid id = Guid.NewGuid();
    private LocalWorkspaceService Workspace => new(Path.Combine(root, "workspace"), Path.Combine(root, "source"));
    private string Source()
    {
        Directory.CreateDirectory(Path.Combine(root, "source"));
        var path = Path.Combine(root, "source", "demo.txt");
        File.WriteAllText(path, "generic demo");
        return path;
    }
    [Fact]
    public async Task Copy_is_deterministic_and_never_overwrites()
    {
        var workspace = Workspace;
        var source = Source();
        var local = await workspace.CopyAsync(id, "demo.txt", source);
        Assert.Equal(workspace.Resolve(id, "demo.txt"), local);
        File.WriteAllText(local, "local edit");
        await Assert.ThrowsAsync<IOException>(() => workspace.CopyAsync(id, "demo.txt", source));
        Assert.Equal("local edit", File.ReadAllText(local));
        workspace.Delete(id, "demo.txt", local);
        Assert.False(File.Exists(local));
        Assert.True(File.Exists(source));
    }
    [Fact]
    public async Task Hashes_match_for_copies_and_change_with_content()
    {
        var source = Source();
        var local = await Workspace.CopyAsync(id, "demo.txt", source);
        var hashes = new FileHashService();
        Assert.Equal(await hashes.HashAsync(source), await hashes.HashAsync(local));
        File.AppendAllText(local, " changed");
        Assert.NotEqual(await hashes.HashAsync(source), await hashes.HashAsync(local));
    }
    [Fact]
    public void Deletion_rejects_paths_outside_the_expected_document()
    {
        var source = Source();
        Assert.Throws<IOException>(() => Workspace.Delete(id, "demo.txt", source));
        Assert.True(File.Exists(source));
        Assert.Throws<ArgumentException>(() => Workspace.Resolve(id, "../demo.txt"));
        Assert.Throws<ArgumentException>(() => new LocalWorkspaceService(root, Path.Combine(root, "source")));
    }
    [Fact]
    public async Task Staged_delete_rolls_back_until_completed()
    {
        var source = Source();
        var path = await Workspace.CopyAsync(id, "demo.txt", source);
        using (Workspace.StageDeletion(id, "demo.txt", path)) Assert.False(File.Exists(path));
        Assert.True(File.Exists(path));
        using (var deletion = Workspace.StageDeletion(id, "demo.txt", path)) deletion.Complete();
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(source));
    }
    public void Dispose()
    {
        // Only this test's explicitly created unique temporary directory is removed.
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
