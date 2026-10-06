using System.Collections.ObjectModel;
using DocumentWorkflow.Application;
using DocumentWorkflow.Domain;

namespace DocumentWorkflow.App;

public sealed class VersionHistoryViewModel(VersionHistoryItem item)
{
    public int Number => item.Version.VersionNumber;
    public string Version => $"v{Number}";
    public bool IsCurrent => item.IsCurrent;
    public string CurrentBadge => IsCurrent ? "Current" : "";
    public string Created => item.Version.CreatedAt.ToLocalTime().ToString("dd MMM yyyy HH:mm:ss");
    public string CompactHash => item.Version.FileHash[..Math.Min(16, item.Version.FileHash.Length)] + "…";
    public string BaseVersion => item.Version.BaseVersion is { } number ? $"v{number}" : "Not recorded";
    public string Origin => item.Version.ChangeNote switch
    {
        "Local check-in." => "Check-in",
        "Competing local writer." => "Competing writer",
        "Initial generic demo file." => "Initial document",
        _ => "Not recorded"
    };
    public string Details => $"Document ID: {item.Version.DocumentId}\nVersion ID: {item.Version.Id}\nVersion: {Version} — {(IsCurrent ? "Current" : "Historical")}\nCreated (UTC): {item.Version.CreatedAt:O}\nBase version: {BaseVersion}\nOrigin: {Origin}\nRecorded note: {item.Version.ChangeNote}\nSHA-256: {item.Version.FileHash}\nArtifact: {item.ArtifactPath}";
}

public sealed class WorkflowEventViewModel(WorkflowEvent value)
{
    public string Timestamp => value.OccurredAt.ToLocalTime().ToString("dd MMM yyyy HH:mm:ss");
    public string Label => value.Type switch
    {
        WorkflowEventType.DocumentCreated => "Document created",
        WorkflowEventType.CheckoutCreated => $"Checked out from v{value.BaseVersion}",
        WorkflowEventType.CheckInCompleted => $"Checked in as v{value.VersionNumber}",
        WorkflowEventType.ConflictDetected => $"Conflict detected: base v{value.BaseVersion}, current v{value.VersionNumber}",
        WorkflowEventType.CheckoutDiscarded => "Local checkout discarded",
        WorkflowEventType.ConflictDiscarded => "Conflicting local checkout discarded",
        WorkflowEventType.CompetingVersionPublished => $"Competing writer published v{value.VersionNumber}",
        WorkflowEventType.RecoveryPerformed => "Recovery performed",
        _ => "Workflow event"
    };
    public string Context => value.Details ?? (value.BaseVersion is { } number
        ? $"Base v{number}; current/result v{value.VersionNumber}." : $"Version v{value.VersionNumber}.");
    public string Details => $"{Label}\nUTC: {value.OccurredAt:O}\n{Context}\nEvent ID: {value.Id}\nDocument ID: {value.DocumentId}\nCheckout timestamp (UTC): {value.CheckoutAt:O}";
}

public sealed class HistoryViewModel : ObservableViewModel
{
    private readonly IDocumentWorkflowService workflow;
    private VersionHistoryViewModel? selectedVersion;
    private WorkflowEventViewModel? selectedEvent;
    private bool loading;
    public HistoryViewModel(Guid documentId, IDocumentWorkflowService workflow)
    {
        DocumentId = documentId;
        this.workflow = workflow;
        RefreshCommand = new AsyncCommand(RefreshAsync, ReportError);
        OpenVersionCommand = new AsyncCommand(async () =>
        {
            if (SelectedVersion is not { } selected) return;
            await workflow.OpenVersionAsync(DocumentId, selected.Number);
            Message = $"Opened {selected.Version} in a separate inspection copy. Checkout and current version are unchanged.";
            Notify(nameof(Message));
        }, ReportError, () => SelectedVersion is not null && !loading);
    }
    public Guid DocumentId { get; }
    public string Title { get; private set; } = "Document history";
    public string CurrentVersion { get; private set; } = "";
    public string CheckoutBase { get; private set; } = "No active checkout";
    public string LocalState { get; private set; } = "Available";
    public bool IsConflict { get; private set; }
    public string Message { get; private set; } = "";
    public string AuditNotice => "Newest first • Events are recorded from Phase 6 onward. Earlier events are not reconstructed.";
    public string EmptyEvents => Events.Count == 0 ? "No workflow events recorded for this document yet." : "";
    public ObservableCollection<VersionHistoryViewModel> Versions { get; } = [];
    public ObservableCollection<WorkflowEventViewModel> Events { get; } = [];
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand OpenVersionCommand { get; }
    public VersionHistoryViewModel? SelectedVersion
    {
        get => selectedVersion;
        set { selectedVersion = value; Notify(); Notify(nameof(VersionDetails)); OpenVersionCommand.NotifyCanExecuteChanged(); }
    }
    public WorkflowEventViewModel? SelectedEvent
    {
        get => selectedEvent;
        set { selectedEvent = value; Notify(); Notify(nameof(EventDetails)); }
    }
    public string VersionDetails => SelectedVersion?.Details ?? "Select a version to inspect its metadata.";
    public string EventDetails => SelectedEvent?.Details ?? "Select an event for technical details.";
    public async Task RefreshAsync()
    {
        if (loading) return;
        loading = true;
        OpenVersionCommand.NotifyCanExecuteChanged();
        try
        {
            var history = await workflow.GetHistoryAsync(DocumentId);
            var selected = SelectedVersion?.Number;
            Title = $"{history.Context.Document.LogicalName} — history";
            CurrentVersion = $"Current: v{history.Context.Document.CurrentVersion}";
            CheckoutBase = history.Context.WorkingCopy is { } copy ? $"Your checkout: based on v{copy.BaseVersion}" : "No active checkout";
            IsConflict = history.Context.Evaluation?.State == WorkingCopyState.Conflict;
            LocalState = history.Context.WorkingCopy is null ? "Available" : history.Context.WorkspaceWarning
                ?? history.Context.Evaluation?.State?.ToString() ?? "State unavailable";
            Versions.Clear();
            foreach (var item in history.Versions) Versions.Add(new(item));
            Events.Clear();
            foreach (var item in history.Events) Events.Add(new(item));
            SelectedVersion = Versions.FirstOrDefault(x => x.Number == selected) ?? Versions.FirstOrDefault();
            SelectedEvent = Events.FirstOrDefault();
            Message = "";
            foreach (var name in new[] { nameof(Title), nameof(CurrentVersion), nameof(CheckoutBase), nameof(LocalState), nameof(IsConflict), nameof(Message), nameof(EmptyEvents) }) Notify(name);
        }
        finally { loading = false; OpenVersionCommand.NotifyCanExecuteChanged(); }
    }
    private void ReportError(Exception error) { Message = error is WorkflowException ? error.Message : "Could not load history. Refresh to try again."; Notify(nameof(Message)); }
}
