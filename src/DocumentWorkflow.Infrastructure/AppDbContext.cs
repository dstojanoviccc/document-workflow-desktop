using DocumentWorkflow.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DocumentWorkflow.Infrastructure;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<DocumentRecord> Documents => Set<DocumentRecord>();
    public DbSet<DocumentVersion> Versions => Set<DocumentVersion>();
    public DbSet<WorkingCopy> WorkingCopies => Set<WorkingCopy>();

    protected override void OnModelCreating(ModelBuilder model)
    {
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
}

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=document-workflow.db").Options);
}
