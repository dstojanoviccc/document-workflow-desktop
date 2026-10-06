namespace DocumentWorkflow.Application;

public interface IVersionContentStore
{
    string Resolve(Guid documentId, int versionNumber, string fileName);
    Task<string> CreateAsync(Guid documentId, int versionNumber, string fileName, string workingPath,
        string expectedHash, CancellationToken cancellationToken = default);
    void Delete(Guid documentId, int versionNumber, string fileName);
    bool IsManagedPath(string path) => false;
}
