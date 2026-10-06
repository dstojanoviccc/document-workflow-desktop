using System.Collections.ObjectModel;
using DocumentWorkflow.Application;
using DocumentWorkflow.Domain;
using Microsoft.Extensions.Logging;

namespace DocumentWorkflow.App;

public sealed class DocumentListItemViewModel
{
    private readonly DocumentSnapshot snapshot;
    public DocumentListItemViewModel(DocumentSnapshot snapshot, Action<DocumentListItemViewModel> showDetails,
        Func<DocumentListItemViewModel, string, Task> run, Action<Exception> onError)
    {
        this.snapshot = snapshot;
        ViewDetailsCommand = new RelayCommand(() => showDetails(this));
        CheckOutCommand = new AsyncCommand(() => run(this, "checkout"), onError);
        OpenCommand = new AsyncCommand(() => run(this, "open"), onError);
        DiscardCommand = new AsyncCommand(() => run(this, "discard"), onError);
        CheckInCommand = new AsyncCommand(() => run(this, "checkin"), onError, () => CanCheckIn);
        OpenLatestCommand = new AsyncCommand(() => run(this, "latest"), onError);
        SaveLocalCommand = new AsyncCommand(() => run(this, "savecopy"), onError);
        KeepLocalCommand = new AsyncCommand(() => run(this, "keep"), onError);
    }
    public Guid Id => snapshot.Document.Id;
    public string LogicalName => snapshot.Document.LogicalName;
    public string FileName => snapshot.Document.FileName;
    public string Version => $"v{snapshot.Document.CurrentVersion}";
    public WorkingCopyState? WorkingState => snapshot.Evaluation?.State;
    public bool IsModified => WorkingState == WorkingCopyState.Modified;
    public bool IsConflict => WorkingState == WorkingCopyState.Conflict;
    public bool IsStaleBase => snapshot.WorkingCopy is { } copy && copy.BaseVersion != snapshot.Document.CurrentVersion;
    public string RecoveryLabel => IsStaleBase ? "Details / Recover" : "View Details";
    public string OpenLabel => IsConflict ? "Open my copy" : "Open";
    public string ConflictExplanation => !IsStaleBase ? "" : IsConflict
        ? $"Your edits still exist, but {Version} is now current. This checkout is based on {BaseVersion}; check-in is blocked."
        : $"This checkout is based on {BaseVersion}, while {Version} is current. Keep or save the local copy, or discard and check out current.";
    public bool RequiresModifiedDiscardWarning => IsModified || IsConflict || snapshot.Evaluation is null || snapshot.Evaluation.Issue == EvaluationIssue.Unreadable;
    public string Status => !HasWorkingCopy ? snapshot.Document.Status.ToString()
        : snapshot.Evaluation?.Issue == EvaluationIssue.Missing ? "Local file missing"
        : snapshot.Evaluation?.Issue == EvaluationIssue.Unreadable ? WorkingState is { } state ? $"{state} (unverified)" : "State unavailable"
        : WorkingState?.ToString() ?? "State not evaluated";
    public string CurrentHash => snapshot.Evaluation?.CurrentHash is { } hash ? hash[..Math.Min(16, hash.Length)] + "…" : "Not available";
    public string ContentsComparison => snapshot.Evaluation?.Issue != EvaluationIssue.None ? "Local contents could not be checked."
        : IsModified || IsConflict ? "Local contents differ from the checked-out version." : "Local contents match the checked-out version.";
    public bool HasWorkingCopy => snapshot.WorkingCopy is not null;
    public bool CanCheckOut => !HasWorkingCopy && snapshot.Document.Status == WorkingCopyState.Available;
    public bool CanOpen => HasWorkingCopy && snapshot.WorkspaceWarning is null;
    public bool CanCheckIn => HasWorkingCopy && IsModified && !HasWarning && snapshot.Evaluation?.Issue == EvaluationIssue.None;
    public bool HasWarning => snapshot.WorkspaceWarning is not null;
    public string Warning => snapshot.WorkspaceWarning ?? "";
    public string Updated => snapshot.Document.UpdatedAt.ToLocalTime().ToString("dd MMM yyyy HH:mm");
    public string LocalPath => snapshot.WorkingCopy?.LocalPath ?? "";
    public string CheckedOutAt => snapshot.WorkingCopy?.CheckedOutAt.ToLocalTime().ToString("dd MMM yyyy HH:mm") ?? "";
    public string BaseVersion => snapshot.WorkingCopy is { } copy ? $"v{copy.BaseVersion}" : "";
    public string BaseHash => snapshot.WorkingCopy is { } copy ? copy.LastKnownHash[..Math.Min(16, copy.LastKnownHash.Length)] + "…" : "";
    public RelayCommand ViewDetailsCommand { get; }
    public AsyncCommand CheckOutCommand { get; }
    public AsyncCommand OpenCommand { get; }
    public AsyncCommand DiscardCommand { get; }
    public AsyncCommand CheckInCommand { get; }
    public AsyncCommand OpenLatestCommand { get; }
    public AsyncCommand SaveLocalCommand { get; }
    public AsyncCommand KeepLocalCommand { get; }
}

public sealed class MainViewModel : ObservableViewModel
{
    private readonly IDocumentWorkflowService workflow;
    private readonly IUserDialogService dialogs;
    private readonly ILogger<MainViewModel> logger;
    private bool initialized;
    private bool activationPending;
    public MainViewModel(IDocumentWorkflowService workflow, IUserDialogService dialogs, ILogger<MainViewModel> logger)
    {
        this.workflow = workflow;
        this.dialogs = dialogs;
        this.logger = logger;
        RefreshCommand = new AsyncCommand(RefreshAsync, ReportError);
        CloseDetailsCommand = new RelayCommand(() => { DetailsVisible = false; Notify(nameof(DetailsVisible)); });
    }
    public ObservableCollection<DocumentListItemViewModel> Documents { get; } = [];
    public AsyncCommand RefreshCommand { get; }
    public RelayCommand CloseDetailsCommand { get; }
    public bool IsBusy { get; private set; }
    public string Message { get; private set; } = "Loading documents…";
    public string CountText => $"{Documents.Count} documents";
    public bool DetailsVisible { get; private set; }
    public DocumentListItemViewModel? SelectedDocument { get; private set; }
    private void Busy(bool value) { IsBusy = value; Notify(nameof(IsBusy)); }
    private void SetMessage(string text) { Message = text; Notify(nameof(Message)); }
    private async Task LoadAsync()
    {
        var documents = await workflow.ListAsync();
        initialized = true;
        var selectedId = SelectedDocument?.Id;
        Documents.Clear();
        foreach (var document in documents) Documents.Add(new(document, ShowDetails, RunActionAsync, ReportError));
        SelectedDocument = Documents.FirstOrDefault(x => x.Id == selectedId);
        DetailsVisible = DetailsVisible && SelectedDocument is not null;
        Notify(nameof(SelectedDocument));
        Notify(nameof(DetailsVisible));
        Notify(nameof(CountText));
        logger.LogInformation("Loaded {DocumentCount} documents with {WorkingCopyCount} active working copies", documents.Count, documents.Count(x => x.WorkingCopy is not null));
    }
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        Busy(true);
        SetMessage("Loading documents…");
        try
        {
            await LoadAsync();
            var warnings = Documents.Count(x => x.HasWarning);
            SetMessage(Documents.Count == 0 ? "No documents found." : warnings > 0
                ? $"{warnings} local working copy needs attention. View Details for guidance."
                : $"Loaded from SQLite • refreshed {DateTime.Now:HH:mm:ss}");
        }
        catch (Exception error) { ReportError(error); }
        finally { await FinishAsync(); }
    }
    public async Task OnActivatedAsync()
    {
        if (!initialized) return; // Initial startup has its own load.
        if (IsBusy) { activationPending = true; return; }
        await RefreshAsync();
    }
    private async Task FinishAsync()
    {
        Busy(false);
        if (!activationPending) return;
        activationPending = false;
        await OnActivatedAsync();
    }
    private async Task RunActionAsync(DocumentListItemViewModel row, string action)
    {
        if (IsBusy) return;
        Busy(true);
        SetMessage(action switch { "checkout" => "Checking out document…", "checkin" => "Checking in document…", "discard" => "Discarding local checkout…", _ => "Opening local copy…" });
        try
        {
            if (action == "savecopy")
            {
                var destination = dialogs.SelectLocalCopyDestination(row.FileName);
                if (destination is null) { SetMessage("Save cancelled. Your local edits have been kept."); return; }
                await workflow.SaveLocalCopyAsync(row.Id, destination);
                SetMessage("Local copy saved. The checkout and its edits remain intact.");
                return;
            }
            if (action == "keep")
            {
                SetMessage("Your local edits have been kept. The checkout remains active.");
                return;
            }
            var allowModified = false;
            if (action == "discard")
            {
                await LoadAsync(); // Do not base a destructive warning on a stale row.
                var currentRow = Documents.SingleOrDefault(x => x.Id == row.Id)
                    ?? throw new WorkflowException("The document no longer exists. Refresh the library.");
                allowModified = currentRow.RequiresModifiedDiscardWarning;
                logger.LogInformation("Discard confirmation requested for {DocumentId}; warn about local edits: {WarnForEdits}", row.Id, allowModified);
                if (!dialogs.ConfirmDiscard(currentRow.FileName, allowModified))
                {
                    SetMessage("Discard cancelled. The local working copy has been kept.");
                    return;
                }
            }
            string? cleanupWarning = null;
            CheckInResult? checkInResult = null;
            switch (action)
            {
                case "checkout": await workflow.CheckOutAsync(row.Id); break;
                case "checkin": checkInResult = await workflow.CheckInWithResultAsync(row.Id); cleanupWarning = checkInResult.Message; break;
                case "latest": await workflow.OpenLatestAsync(row.Id); break;
                case "discard": cleanupWarning = await workflow.DiscardAsync(row.Id, allowModified: allowModified); break;
                case "open": await workflow.OpenAsync(row.Id); break;
            }
            await LoadAsync();
            if (checkInResult?.Conflict is not null) ShowDetails(Documents.Single(x => x.Id == row.Id));
            SetMessage(cleanupWarning ?? action switch
            {
                "checkout" => $"{row.FileName} checked out. Open the local copy to edit it.",
                "checkin" => $"{row.FileName} checked in as {Documents.Single(x => x.Id == row.Id).Version}. The previous version is retained.",
                "discard" => $"Checkout discarded for {row.FileName}. The source file is unchanged.",
                "latest" => "Opened a separate read-only copy of the latest version. Your checkout is unchanged.",
                _ => $"Opened {row.FileName} in its default Windows application."
            });
        }
        catch (Exception error)
        {
            // Refresh filesystem presence after a failed Open/discard without hiding the operation error.
            try { await LoadAsync(); }
            catch (Exception readError) { logger.LogError(readError, "Library refresh after workflow failure failed"); }
            ReportError(error);
        }
        finally { await FinishAsync(); }
    }
    private void ShowDetails(DocumentListItemViewModel document)
    {
        SelectedDocument = document;
        DetailsVisible = true;
        Notify(nameof(SelectedDocument));
        Notify(nameof(DetailsVisible));
    }
    private void ReportError(Exception error)
    {
        logger.LogError(error, "Document library operation failed");
        SetMessage(error is WorkflowException ? error.Message : "Could not load documents. Check the application log and try Refresh again.");
    }
}
