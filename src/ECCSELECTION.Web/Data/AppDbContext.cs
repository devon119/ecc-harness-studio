using ECCSELECTION.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace ECCSELECTION.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<EccComponent> EccComponents => Set<EccComponent>();
    public DbSet<SyncLog> SyncLogs => Set<SyncLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<EccComponent>(entity =>
        {
            entity.ToTable("EccComponents");
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.Path).IsUnique();

            entity.Property(e => e.ComponentType)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            entity.Property(e => e.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(e => e.Path)
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(e => e.Sha)
                .HasMaxLength(64)
                .IsRequired();

            entity.Property(e => e.RawUrl).IsRequired();
            entity.Property(e => e.HtmlUrl).IsRequired();
            entity.Property(e => e.DescriptionEn).IsRequired();
            entity.Property(e => e.SummaryZh).IsRequired();
            entity.Property(e => e.SdlcRoles).IsRequired();
            entity.Property(e => e.TechStacks).IsRequired();
            entity.Property(e => e.ArchStyles).IsRequired();
            entity.Property(e => e.UsageGuide).IsRequired();
        });

        modelBuilder.Entity<SyncLog>(entity =>
        {
            entity.ToTable("SyncLogs");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
        });
    }
}
