using DocumentWorkflow.Application;
using DocumentWorkflow.Infrastructure;

namespace DocumentWorkflow.Tests;

public sealed class VersionContentTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "version-tests-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task Artifact_hash_matches_stored_bytes_and_existing_artifact_is_never_overwritten()
    {
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, "input.xlsx");
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "demo-data", "Product-Catalog.xlsx"));
        await File.WriteAllBytesAsync(input, bytes);
        var hashes = new FileHashService();
        var store = new LocalVersionContentStore(Path.Combine(root, "versions"), Path.Combine(root, "workspace"), Path.Combine(root, "source"), hashes);
        var id = Guid.NewGuid();
        var hash = await hashes.HashAsync(input);
        Assert.Equal(hash, await store.CreateAsync(id, 2, "catalog.xlsx", input, hash));
        var path = store.Resolve(id, 2, "catalog.xlsx");
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        await Assert.ThrowsAsync<IOException>(() => store.CreateAsync(id, 2, "catalog.xlsx", input, hash));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        await Assert.ThrowsAsync<WorkflowException>(() => store.CreateAsync(id, 3, "catalog.xlsx", input, "stale hash"));
        Assert.False(File.Exists(store.Resolve(id, 3, "catalog.xlsx")));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(input));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
