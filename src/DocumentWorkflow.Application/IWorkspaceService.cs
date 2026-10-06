namespace DocumentWorkflow.Application;

public interface IWorkspaceService
{
    string Root { get; }
    string Resolve(Guid documentId, string fileName);
    Task<string> CopyAsync(Guid documentId, string fileName, string sourcePath, CancellationToken cancellationToken = default);
    bool Exists(Guid documentId, string fileName, string storedPath);
    void Delete(Guid documentId, string fileName, string storedPath);
    IStagedDeletion StageDeletion(Guid documentId, string fileName, string storedPath);
    Task ExportAsync(Guid documentId, string fileName, string storedPath, string destination, CancellationToken cancellationToken = default);
    Task<string> CreateInspectionAsync(Guid documentId, int version, string fileName, string sourcePath, CancellationToken cancellationToken = default);
}

public interface IStagedDeletion : IDisposable
{
    string StagedPath { get; }
    IDisposable AcquireReadLock();
    void Complete();
}

public interface IFileHashService
{
    Task<string> HashAsync(string path, CancellationToken cancellationToken = default);
}
