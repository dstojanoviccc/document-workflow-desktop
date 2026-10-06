namespace DocumentWorkflow.Domain;

public enum WorkingCopyState { Available, CheckedOut, Unchanged, Modified, ReadyToCheckIn, Conflict }

public sealed class DocumentRecord
{
    private DocumentRecord() { }
    public DocumentRecord(string logicalName, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName.IndexOfAny(['/', '\\']) >= 0)
            throw new ArgumentException("Use a file name without directories.", nameof(fileName));
        LogicalName = logicalName.Trim();
        FileName = fileName.Trim();
        FileExtension = Path.GetExtension(FileName).ToLowerInvariant();
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string LogicalName { get; private set; } = "";
    public string FileName { get; private set; } = "";
    public string FileExtension { get; private set; } = "";
    public int CurrentVersion { get; private set; } = 1;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;
    public WorkingCopyState Status { get; private set; } = WorkingCopyState.Available;
    public void CheckOut()
    {
        if (Status != WorkingCopyState.Available) throw new InvalidOperationException("This document is already checked out.");
        Status = WorkingCopyState.CheckedOut;
    }
    public void DiscardCheckout()
    {
        if (Status == WorkingCopyState.Available) throw new InvalidOperationException("This document has no active checkout.");
        Status = WorkingCopyState.Available;
    }
    public void CompleteCheckIn(int baseVersion, int newVersion, DateTime createdAt)
    {
        if (Status == WorkingCopyState.Available || baseVersion != CurrentVersion || newVersion != checked(CurrentVersion + 1))
            throw new InvalidOperationException("The checkout base must match the current version before check-in.");
        CurrentVersion = newVersion;
        UpdatedAt = createdAt;
        Status = WorkingCopyState.Available;
    }
}

public sealed class DocumentVersion
{
    private DocumentVersion() { }
    public DocumentVersion(Guid documentId, int versionNumber, string fileHash, string changeNote, int? baseVersion = null)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("Document ID is required.", nameof(documentId));
        if (versionNumber < 1) throw new ArgumentOutOfRangeException(nameof(versionNumber));
        ArgumentException.ThrowIfNullOrWhiteSpace(fileHash);
        DocumentId = documentId;
        VersionNumber = versionNumber;
        FileHash = fileHash;
        ChangeNote = changeNote ?? "";
        if (baseVersion is { } origin && (origin < 1 || origin >= versionNumber))
            throw new ArgumentOutOfRangeException(nameof(baseVersion));
        BaseVersion = baseVersion;
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid DocumentId { get; private set; }
    public int VersionNumber { get; private set; }
    public string FileHash { get; private set; } = "";
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public string ChangeNote { get; private set; } = "";
    public int? BaseVersion { get; private set; }
    public void AttachDemoContent(string fileHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileHash);
        if (VersionNumber != 1 || !ChangeNote.StartsWith("Demo metadata")) return;
        FileHash = fileHash;
        ChangeNote = "Initial generic demo file.";
    }
}

public sealed class WorkingCopy
{
    private WorkingCopy() { }
    public WorkingCopy(Guid documentId, string localPath, int baseVersion, string lastKnownHash)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("Document ID is required.", nameof(documentId));
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastKnownHash);
        if (baseVersion < 1) throw new ArgumentOutOfRangeException(nameof(baseVersion));
        DocumentId = documentId;
        LocalPath = localPath;
        BaseVersion = baseVersion;
        LastKnownHash = lastKnownHash;
    }
    public Guid DocumentId { get; private set; }
    public string LocalPath { get; private set; } = "";
    public int BaseVersion { get; private set; }
    public DateTime CheckedOutAt { get; private set; } = DateTime.UtcNow;
    public string LastKnownHash { get; private set; } = "";
    public WorkingCopyState State { get; private set; } = WorkingCopyState.CheckedOut;
}
