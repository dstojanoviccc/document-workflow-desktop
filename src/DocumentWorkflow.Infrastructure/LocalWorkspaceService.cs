using DocumentWorkflow.Application;

namespace DocumentWorkflow.Infrastructure;

public sealed class LocalWorkspaceService : IWorkspaceService
{
    public string Root { get; }
    public LocalWorkspaceService(string root, string sourceRoot)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (Root.Equals(Path.GetPathRoot(Root), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use a dedicated directory for the workspace, not a drive root.");
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot));
        if (Inside(Root, source) || Inside(source, Root))
            throw new ArgumentException("The workspace and demo repository must be separate directories.");
        SafePaths.RejectLinks(Root);
    }
    private static bool Inside(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public string Resolve(Guid documentId, string fileName)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("A document ID is required.", nameof(documentId));
        SafePaths.ValidateFileName(fileName);
        var path = Path.Combine(Root, documentId.ToString("N"), fileName);
        SafePaths.RejectLinks(path);
        return path;
    }
    private string Validate(Guid id, string name, string storedPath)
    {
        var expected = Resolve(id, name);
        if (!string.Equals(expected, Path.GetFullPath(storedPath), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The stored local path does not match this workspace. Restore the workspace configuration before continuing.");
        return expected;
    }
    public async Task<string> CopyAsync(Guid documentId, string fileName, string sourcePath, CancellationToken cancellationToken = default)
    {
        var target = Resolve(documentId, fileName);
        if (Inside(Path.GetFullPath(sourcePath), Root)) throw new IOException("The source file cannot be inside the workspace.");
        SafePaths.RejectLinks(sourcePath);
        // Open source before creating any target; CreateNew never overwrites an existing local file.
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        SafePaths.RejectLinks(target);
        var created = false;
        try
        {
            await using var local = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            created = true;
            await source.CopyToAsync(local, cancellationToken);
            await local.FlushAsync(cancellationToken);
        }
        catch
        {
            if (created) Delete(documentId, fileName, target);
            throw;
        }
        return target;
    }
    public bool Exists(Guid documentId, string fileName, string storedPath) => File.Exists(Validate(documentId, fileName, storedPath));
    public void Delete(Guid documentId, string fileName, string storedPath)
    {
        var path = Validate(documentId, fileName, storedPath);
        File.Delete(path);
        RemoveEmptyDirectory(path);
    }
    private static void RemoveEmptyDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, false);
    }
    public IStagedDeletion StageDeletion(Guid documentId, string fileName, string storedPath)
    {
        var path = Validate(documentId, fileName, storedPath);
        if (Directory.Exists(path)) throw new IOException("The expected local file is a directory. Discard has been stopped to protect its contents.");
        var staged = path + ".discard";
        SafePaths.RejectLinks(staged);
        if (File.Exists(staged)) throw new IOException("A previous discard could not finish cleanup. Remove its leftover .discard file before trying again.");
        var moved = File.Exists(path);
        if (moved) File.Move(path, staged, false);
        return new StagedDeletion(path, staged, moved);
    }
    private sealed class StagedDeletion(string path, string staged, bool moved) : IStagedDeletion
    {
        private bool completed;
        public void Complete()
        {
            // After metadata commit, never restore an orphan as an active working file.
            completed = true;
            SafePaths.RejectLinks(staged);
            if (moved) File.Delete(staged);
            RemoveEmptyDirectory(path);
        }
        public void Dispose()
        {
            if (!completed && moved)
            {
                SafePaths.RejectLinks(path);
                File.Move(staged, path, false);
            }
        }
    }
}
