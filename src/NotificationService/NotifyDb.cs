using Microsoft.EntityFrameworkCore;

namespace NotificationService;

public class NotifyDb : DbContext
{
    public NotifyDb(DbContextOptions<NotifyDb> options) : base(options) { }

    public DbSet<NotificationLog> Notifications => Set<NotificationLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Environment.GetEnvironmentVariable("ORACLE_SCHEMA") ?? "PROCESSCHECKER");

        b.Entity<NotificationLog>(e =>
        {
            e.ToTable("PC_NOTIFICATIONS");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ToList).HasMaxLength(2000);
            e.Property(x => x.Subject).HasMaxLength(300);
            e.Property(x => x.Body).HasColumnType("CLOB");
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Error).HasMaxLength(1000);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CreatedAt);
        });
    }
}