using DocumentWorkflow.Application;

namespace DocumentWorkflow.Infrastructure;

public sealed class DemoDocumentSource(string root) : IDocumentSource
{
    public string Root { get; } = Path.GetFullPath(root);
    public string Resolve(string fileName)
    {
        SafePaths.ValidateFileName(fileName);
        var path = Path.Combine(Root, fileName);
        SafePaths.RejectLinks(path);
        if (!File.Exists(path)) throw new FileNotFoundException("The demo source file is missing. Restore the packaged demo data and try again.", path);
        return path;
    }
}

internal static class SafePaths
{
    public static void ValidateFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' ') || name is "." or "..")
            throw new ArgumentException("The document file name is not safe.", nameof(name));
        var stem = name.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Reserved Windows file names are not supported.", nameof(name));
    }
    public static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked files and directories are not allowed in document storage.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
