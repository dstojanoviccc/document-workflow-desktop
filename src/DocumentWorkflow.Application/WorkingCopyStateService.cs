using System.Collections.Concurrent;
using DocumentWorkflow.Domain;
using Microsoft.Extensions.Logging;

namespace DocumentWorkflow.Application;

public enum EvaluationIssue { None, Missing, Unreadable }
public sealed record WorkingCopyEvaluation(WorkingCopyState? State, string? CurrentHash,
    EvaluationIssue Issue = EvaluationIssue.None, string? Warning = null);

// Evaluated state is derived, never written to SQLite. Cache only the last reliable
// observation within this process so temporary read failures do not invent a state.
public sealed class WorkingCopyStateService(IWorkspaceService workspace, IFileHashService hashes, ILogger logger)
{
    private readonly ConcurrentDictionary<Baseline, WorkingCopyState> reliable = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private sealed record Baseline(Guid Id, string Path, int Version, string Hash, DateTime CheckedOutAt);

    public async Task<WorkingCopyEvaluation> EvaluateAsync(DocumentRecord document, WorkingCopy copy,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(async () =>
            {
                var key = new Baseline(document.Id, copy.LocalPath, copy.BaseVersion, copy.LastKnownHash, copy.CheckedOutAt);
                logger.LogInformation("Working copy evaluation started for {DocumentId}", document.Id);
                try
                {
                    if (!workspace.Exists(document.Id, document.FileName, copy.LocalPath))
                        return Missing(document.Id);
                    var current = await hashes.HashAsync(copy.LocalPath, cancellationToken).ConfigureAwait(false);
                    var state = string.Equals(current, copy.LastKnownHash, StringComparison.OrdinalIgnoreCase)
                        ? WorkingCopyState.Unchanged : copy.BaseVersion != document.CurrentVersion
                            ? WorkingCopyState.Conflict : WorkingCopyState.Modified;
                    if (reliable.TryGetValue(key, out var previous) && previous != state)
                        logger.LogInformation("Working state changed for {DocumentId}: {PreviousState} to {CurrentState}", document.Id, previous, state);
                    reliable[key] = state;
                    logger.LogInformation("Working copy evaluation completed for {DocumentId}: {WorkingState}", document.Id, state);
                    return new WorkingCopyEvaluation(state, current);
                }
                catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
                { return Missing(document.Id); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    logger.LogWarning(error, "Working copy hash/read evaluation failed for {DocumentId}", document.Id);
                    return new WorkingCopyEvaluation(reliable.TryGetValue(key, out var last) ? last : null, null,
                        EvaluationIssue.Unreadable, "The local file could not be evaluated. Close the external editor or check workspace permissions, then Refresh. Any displayed state is the last reliable observation.");
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    private WorkingCopyEvaluation Missing(Guid id)
    {
        logger.LogWarning("Working copy missing for {DocumentId}", id);
        return new(null, null, EvaluationIssue.Missing,
            "The local file is missing. Open is unavailable. Discard clears this checkout; no file is recreated automatically.");
    }
    public void Forget(Guid id)
    {
        foreach (var key in reliable.Keys.Where(key => key.Id == id)) reliable.TryRemove(key, out _);
    }
}
