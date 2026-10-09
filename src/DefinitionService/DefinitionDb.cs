using Microsoft.EntityFrameworkCore;

namespace DefinitionService;

public class DefinitionDb : DbContext
{
    public DefinitionDb(DbContextOptions<DefinitionDb> options) : base(options) { }

    public DbSet<DefinitionEntity> Definitions => Set<DefinitionEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<DefinitionEntity>(e =>
        {
            e.HasIndex(x => new { x.Key, x.Version }).IsUnique();
            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.Name).HasMaxLength(200);
        });
    }
}