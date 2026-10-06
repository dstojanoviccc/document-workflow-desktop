using DocumentWorkflow.Application;
using DocumentWorkflow.Domain;
using Microsoft.Extensions.Logging;

namespace DocumentWorkflow.Infrastructure;

// Caller holds the same SQLite writer transaction used for workflow mutations.
// Never delete content: restore active staging or quarantine exact known unreferenced paths.
public sealed class LocalWorkflowRecovery(LocalWorkspaceService workspace, LocalVersionContentStore storage,
    string recoveryRoot, ILogger<LocalWorkflowRecovery> logger) : IWorkflowRecovery
{
    public Task<string?> RecoverAsync(DocumentRecord document, WorkingCopy? copy, IReadOnlyList<DocumentVersion> versions,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var notices = new List<string>();
        void Attempt(Action action, string label)
        {
            try { cancellationToken.ThrowIfCancellationRequested(); action(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                logger.LogWarning(error, "Recovery deferred for {DocumentId}: {Artifact}", document.Id, label);
                notices.Add($"Recovery could not inspect {label}. Close external editors, check storage permissions and Refresh.");
            }
        }
        void Quarantine(string path)
        {
            SafePaths.RejectLinks(path);
            var root = Path.GetFullPath(recoveryRoot);
            SafePaths.RejectLinks(root);
            Directory.CreateDirectory(root);
            var destination = Path.Combine(root, document.Id.ToString("N") + "-" + Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(path));
            File.Move(path, destination, false);
            logger.LogWarning("Unreferenced internal artifact quarantined for {DocumentId} at {RecoveryPath}", document.Id, destination);
            notices.Add($"Interrupted-operation content preserved in recovery: {destination}");
        }
        Attempt(() =>
        {
            var local = workspace.Resolve(document.Id, document.FileName);
            if (copy is not null && !string.Equals(local, Path.GetFullPath(copy.LocalPath), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Stored checkout belongs to a different workspace.");
            var staged = local + ".discard";
            SafePaths.RejectLinks(staged);
            if (!File.Exists(staged)) return;
            if (copy is not null && !File.Exists(local) && !Directory.Exists(local))
            {
                File.Move(staged, local, false);
                logger.LogWarning("Interrupted checkout staging restored for {DocumentId}", document.Id);
                notices.Add("The local working copy was restored after an interrupted operation.");
            }
            else Quarantine(staged);
        }, "local staging");
        Attempt(() =>
        {
            var directory = Path.GetDirectoryName(Path.GetDirectoryName(storage.Resolve(document.Id, 2, document.FileName)))!;
            if (!Directory.Exists(directory)) return;
            var referenced = versions.Select(x => x.VersionNumber).ToHashSet();
            foreach (var versionDirectory in Directory.EnumerateDirectories(directory))
            {
                var name = Path.GetFileName(versionDirectory);
                if (!int.TryParse(name, out var number) || number < 2 || name != number.ToString(System.Globalization.CultureInfo.InvariantCulture)) continue;
                // Only the exact managed artifact is eligible; unknown sibling files/directories remain untouched.
                var path = storage.Resolve(document.Id, number, document.FileName);
                if (!referenced.Contains(number) && File.Exists(path)) Quarantine(path);
            }
            foreach (var version in versions.Where(x => x.VersionNumber >= 2))
                if (!File.Exists(storage.Resolve(document.Id, version.VersionNumber, document.FileName)))
                    notices.Add($"The committed v{version.VersionNumber} artifact is missing. Restore it from backup; metadata has been retained.");
        }, "version storage");
        return notices.Count == 0 ? null : string.Join(" ", notices);
    }, cancellationToken);
}
