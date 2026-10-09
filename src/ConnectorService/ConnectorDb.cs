using Microsoft.EntityFrameworkCore;

namespace ConnectorService;

public class ConnectorDb : DbContext
{
    public ConnectorDb(DbContextOptions<ConnectorDb> options) : base(options) { }

    public DbSet<ConnectionEntity> Connections => Set<ConnectionEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Environment.GetEnvironmentVariable("ORACLE_SCHEMA") ?? "PROCESSCHECKER");

        b.Entity<ConnectionEntity>(e =>
        {
            e.ToTable("PC_CONNECTIONS");
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Type).HasColumnName("CONN_TYPE").HasMaxLength(30);
            e.Property(x => x.ProtectedConnectionString).HasColumnType("CLOB");
            e.Property(x => x.FilePath).HasMaxLength(1000);
            e.Property(x => x.OptionsJson).HasMaxLength(1000);
            e.Property(x => x.Description).HasMaxLength(500);
        });
    }
}