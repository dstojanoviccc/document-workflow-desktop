using DocumentWorkflow.Application;
using DocumentWorkflow.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DocumentWorkflow.Infrastructure;

public sealed class WorkflowStore(IDbContextFactory<AppDbContext> factory) : IWorkflowStore
{
    public async Task<IReadOnlyList<DocumentSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await (from document in db.Documents.AsNoTracking()
                      join copy in db.WorkingCopies.AsNoTracking() on document.Id equals copy.DocumentId into copies
                      from copy in copies.DefaultIfEmpty()
                      orderby document.LogicalName
                      select new DocumentSnapshot(document, copy, null, null)).ToListAsync(cancellationToken);
    }
    public async Task<IWorkflowSession> BeginAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var db = await factory.CreateDbContextAsync(cancellationToken);
        try
        {
            // SQLite's non-deferred write transaction serializes mutations across app instances.
            var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var document = await db.Documents.SingleOrDefaultAsync(x => x.Id == documentId, cancellationToken)
                ?? throw new WorkflowException("The document no longer exists. Refresh the library.");
            var copy = await db.WorkingCopies.SingleOrDefaultAsync(x => x.DocumentId == documentId, cancellationToken);
            var versions = await db.Versions.Where(x => x.DocumentId == documentId).ToListAsync(cancellationToken);
            return new Session(db, transaction, document, copy, versions);
        }
        catch { await db.DisposeAsync(); throw; }
    }
    private sealed class Session(AppDbContext db, IDbContextTransaction transaction, DocumentRecord document, WorkingCopy? copy, IReadOnlyList<DocumentVersion> versions) : IWorkflowSession
    {
        public DocumentRecord Document => document;
        public IReadOnlyList<DocumentVersion> Versions => versions;
        public WorkingCopy? WorkingCopy { get; private set; } = copy;
        public void AddVersion(DocumentVersion version) => db.Versions.Add(version);
        public void Add(WorkingCopy value)
        {
            if (WorkingCopy is not null) throw new WorkflowException("This document already has an active working copy.");
            db.WorkingCopies.Add(value);
            WorkingCopy = value;
        }
        public void RemoveWorkingCopy()
        {
            if (WorkingCopy is null) throw new WorkflowException("This document has no active checkout.");
            db.WorkingCopies.Remove(WorkingCopy);
            WorkingCopy = null;
        }
        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            await db.DisposeAsync();
        }
    }
}
