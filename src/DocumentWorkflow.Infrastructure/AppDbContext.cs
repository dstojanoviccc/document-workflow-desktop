using DocumentWorkflow.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DocumentWorkflow.Infrastructure;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<DocumentRecord> Documents => Set<DocumentRecord>();
    public DbSet<DocumentVersion> Versions => Set<DocumentVersion>();
    public DbSet<WorkingCopy> WorkingCopies => Set<WorkingCopy>();
    public DbSet<WorkflowEvent> WorkflowEvents => Set<WorkflowEvent>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<WorkflowEvent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type).HasConversion<string>();
            entity.HasIndex(x => new { x.DocumentId, x.DeduplicationKey }).IsUnique();
            entity.HasOne<DocumentRecord>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DocumentRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.LogicalName).IsRequired().HasMaxLength(200);
            entity.Property(x => x.FileName).IsRequired().HasMaxLength(255);
            entity.Property(x => x.Status).HasConversion<string>();
        });
        model.Entity<DocumentVersion>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.DocumentId, x.VersionNumber }).IsUnique();
            entity.HasOne<DocumentRecord>().WithMany().HasForeignKey(x => x.DocumentId);
        });
        model.Entity<WorkingCopy>(entity =>
        {
            entity.HasKey(x => x.DocumentId);
            entity.Property(x => x.State).HasConversion<string>();
            entity.HasOne<DocumentRecord>().WithOne().HasForeignKey<WorkingCopy>(x => x.DocumentId);
        });
    }
    private void ProtectAudit()
    {
        if (ChangeTracker.Entries<WorkflowEvent>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Workflow events are append-only.");
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess) { ProtectAudit(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    { ProtectAudit(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }
}

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=document-workflow.db").Options);
}
