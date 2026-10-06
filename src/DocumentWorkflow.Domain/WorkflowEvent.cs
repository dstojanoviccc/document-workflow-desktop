namespace DocumentWorkflow.Domain;

public enum WorkflowEventType
{
    DocumentCreated, CheckoutCreated, CheckInCompleted, ConflictDetected,
    CheckoutDiscarded, ConflictDiscarded, CompetingVersionPublished, RecoveryPerformed
}

// Audit evidence only; document/version/checkout tables remain the source of truth.
public sealed class WorkflowEvent
{
    private WorkflowEvent() { }
    public WorkflowEvent(Guid documentId, WorkflowEventType type, string deduplicationKey, int? versionNumber = null,
        int? baseVersion = null, DateTime? checkoutAt = null, string? details = null, DateTime? occurredAt = null)
    {
        if (documentId == Guid.Empty || string.IsNullOrWhiteSpace(deduplicationKey)) throw new ArgumentException("Event identity is required.");
        Id = Guid.NewGuid(); DocumentId = documentId; Type = type; DeduplicationKey = deduplicationKey;
        VersionNumber = versionNumber; BaseVersion = baseVersion; CheckoutAt = checkoutAt;
        Details = details; OccurredAt = occurredAt ?? DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public WorkflowEventType Type { get; private set; }
    public DateTime OccurredAt { get; private set; }
    public int? VersionNumber { get; private set; }
    public int? BaseVersion { get; private set; }
    public DateTime? CheckoutAt { get; private set; }
    public string? Details { get; private set; }
    public string DeduplicationKey { get; private set; } = "";
}
