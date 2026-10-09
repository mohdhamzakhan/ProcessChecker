using Microsoft.EntityFrameworkCore;

namespace DefinitionService;

public class DefinitionDb : DbContext
{
    public DefinitionDb(DbContextOptions<DefinitionDb> options) : base(options) { }

    public DbSet<DefinitionEntity> Definitions => Set<DefinitionEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Environment.GetEnvironmentVariable("ORACLE_SCHEMA")
                           ?? "PROCESSCHECKER");

        b.Entity<DefinitionEntity>(e =>
        {
            e.ToTable("PC_DEFINITIONS");
            e.HasIndex(x => new { x.Key, x.Version }).IsUnique();
            e.Property(x => x.Key).HasColumnName("DEF_KEY").HasMaxLength(100);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.JsonBody).HasColumnType("CLOB");
        });
    }
}