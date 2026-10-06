using System.IO;

namespace DocumentWorkflow.App;

public sealed record RuntimePaths(string DataDirectory, string WorkspaceDirectory, string SourceDirectory)
{
    public string Database => Path.Combine(DataDirectory, "documents.db");
    public string Versions => Path.Combine(DataDirectory, "versions");
    public string Logs => Path.Combine(DataDirectory, "logs");
    public string Recovery => Path.Combine(DataDirectory, "recovery");
    public static RuntimePaths Resolve(string binaryDirectory, string? data = null, string? workspace = null, string? source = null)
    {
        var binary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(binaryDirectory));
        data = Path.GetFullPath(string.IsNullOrWhiteSpace(data)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DocumentWorkflowDesktop") : data);
        workspace = Path.GetFullPath(string.IsNullOrWhiteSpace(workspace) ? Path.Combine(data, "workspace") : workspace);
        source = Path.GetFullPath(string.IsNullOrWhiteSpace(source) ? Path.Combine(binary, "demo-data") : source);
        if (Inside(data, binary) || Inside(workspace, binary) || string.Equals(data, Path.GetPathRoot(data), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose data and workspace directories outside the application installation directory.");
        return new(data, workspace, source);
    }
    private static bool Inside(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public void CreateDirectories()
    {
        foreach (var directory in new[] { DataDirectory, WorkspaceDirectory, Versions, Logs, Recovery }) Directory.CreateDirectory(directory);
    }
}
