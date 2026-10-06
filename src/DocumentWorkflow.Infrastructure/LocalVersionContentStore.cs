using DocumentWorkflow.Application;

namespace DocumentWorkflow.Infrastructure;

// Version paths are deterministic and never overwritten. The database publishes an artifact
// only after CreateAsync has durably flushed and verified the exact stored bytes.
public sealed class LocalVersionContentStore : IVersionContentStore
{
    public string Root { get; }
    private readonly IFileHashService hashes;
    public LocalVersionContentStore(string root, string workspaceRoot, string sourceRoot, IFileHashService hashes)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        foreach (var other in new[] { workspaceRoot, sourceRoot })
        {
            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(other));
            if (Inside(Root, path) || Inside(path, Root))
                throw new ArgumentException("Version storage must be separate from workspace and demo source directories.");
        }
        if (Root == Path.GetPathRoot(Root)) throw new ArgumentException("Use a dedicated version directory.");
        SafePaths.RejectLinks(Root);
        this.hashes = hashes;
    }
    private static bool Inside(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public string Resolve(Guid documentId, int versionNumber, string fileName)
    {
        if (documentId == Guid.Empty || versionNumber < 2) throw new ArgumentException("A document and version number of at least two are required.");
        SafePaths.ValidateFileName(fileName);
        var path = Path.Combine(Root, documentId.ToString("N"), versionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), fileName);
        SafePaths.RejectLinks(path);
        return path;
    }
    public async Task<string> CreateAsync(Guid documentId, int versionNumber, string fileName, string workingPath,
        string expectedHash, CancellationToken cancellationToken = default)
    {
        var target = Resolve(documentId, versionNumber, fileName);
        var created = false;
        try
        {
            SafePaths.RejectLinks(workingPath);
            // Deny writers and deletion while capturing the file. Existing incompatible editor handles fail safely.
            await using var input = new FileStream(workingPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            SafePaths.RejectLinks(target);
            await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                created = true;
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            var actual = await hashes.HashAsync(target, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new WorkflowException("The working file changed during check-in. Close the editor, Refresh and try again. Your checkout has been kept.");
            return actual;
        }
        catch
        {
            if (created) Delete(documentId, versionNumber, fileName);
            throw;
        }
    }
    public void Delete(Guid documentId, int versionNumber, string fileName) => File.Delete(Resolve(documentId, versionNumber, fileName));
}
