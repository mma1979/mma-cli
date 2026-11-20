using Microsoft.EntityFrameworkCore;
using Mma.Cli.Database.Entities;

namespace Mma.Cli.Database;

public sealed class WorkspaceDbContext : DbContext
{
    public WorkspaceDbContext(DbContextOptions<WorkspaceDbContext> options)
        : base(options)
    {
    }

    public DbSet<WorkspaceRecord> Workspaces => Set<WorkspaceRecord>();
    public DbSet<WorkspaceEntityRecord> Entities => Set<WorkspaceEntityRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<WorkspaceRecord>(entity =>
        {
            entity.ToTable("Workspaces");
            entity.HasKey(w => w.Id);
            entity.Property(w => w.Name).IsRequired();
            entity.Property(w => w.SolutionPath).IsRequired();
            entity.Property(w => w.Mapper).IsRequired();
            entity.HasIndex(w => w.SolutionPath).IsUnique();

            entity.HasMany(w => w.Entities)
                .WithOne(e => e.Workspace)
                .HasForeignKey(e => e.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkspaceEntityRecord>(entity =>
        {
            entity.ToTable("WorkspaceEntities");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EntityName).IsRequired();
            entity.Property(e => e.PkType).IsRequired();
            entity.HasIndex(e => new { e.WorkspaceId, e.EntityName }).IsUnique();
        });
    }
}
