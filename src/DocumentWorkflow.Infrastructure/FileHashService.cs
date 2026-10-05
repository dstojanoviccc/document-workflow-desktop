using System.Security.Cryptography;
using DocumentWorkflow.Application;

namespace DocumentWorkflow.Infrastructure;

public sealed class FileHashService : IFileHashService
{
    public async Task<string> HashAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
    }
}
