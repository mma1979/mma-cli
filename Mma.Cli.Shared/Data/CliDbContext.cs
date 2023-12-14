using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Mma.Cli.Shared.Models;

namespace Mma.Cli.Shared.Data
{
    public class CliDbContext : DbContext
    {
        public CliDbContext()
        {

        }

        public CliDbContext(DbContextOptions<CliDbContext> options) : base(options)
        {

        }

        public DbSet<ProjectModel> Projects { get; set; }
        public DbSet<EntityModel> Entities { get; set; }
        public DbSet<EntityRowModel> EntityRows { get; set; }
        public DbSet<RelationModel> Relations { get; set; }


        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite("name=Default");
            }
        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ProjectModel>(entity =>
            {
                entity.ToTable("Projects");
                entity.HasKey(e => e.Id);

                entity.HasMany(e => e.Entities)
                .WithOne(e => e.Project)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);



            });

            modelBuilder.Entity<EntityModel>(entity =>
            {
                entity.ToTable("Entities");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.HasApi).HasDefaultValue(true);
                entity.Property(e => e.Applied).HasDefaultValue(false);

                entity.HasMany(e => e.Rows)
                .WithOne(e => e.Entity)
                .HasForeignKey(e => e.EntityId)
                .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.ParentRelations)
               .WithOne(e => e.ParentEntity)
               .HasForeignKey(e => e.ParentId)
               .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.ChildRelations)
               .WithOne(e => e.ChiledEntity)
               .HasForeignKey(e => e.ChildId)
               .OnDelete(DeleteBehavior.Cascade);

            });

            modelBuilder.Entity<EntityRowModel>(entity =>
            {
                entity.ToTable("EntityRows");
                entity.HasKey(e => e.Id);

            });

            modelBuilder.Entity<RelationModel>(entity =>
            {
                entity.ToTable("Relations");
                entity.HasKey(e => e.Id);

            });
        }


    }
}
