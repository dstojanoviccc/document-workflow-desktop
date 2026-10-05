namespace DocumentWorkflow.Application;

public interface IWorkspaceService
{
    string Root { get; }
    string Resolve(Guid documentId, string fileName);
    Task<string> CopyAsync(Guid documentId, string fileName, string sourcePath, CancellationToken cancellationToken = default);
    bool Exists(Guid documentId, string fileName, string storedPath);
    void Delete(Guid documentId, string fileName, string storedPath);
    IStagedDeletion StageDeletion(Guid documentId, string fileName, string storedPath);
}

public interface IStagedDeletion : IDisposable
{
    void Complete();
}

public interface IFileHashService
{
    Task<string> HashAsync(string path, CancellationToken cancellationToken = default);
}
