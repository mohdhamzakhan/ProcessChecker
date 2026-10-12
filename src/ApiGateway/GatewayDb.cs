using Microsoft.EntityFrameworkCore;

namespace ApiGateway;

public class GatewayDb : DbContext
{
    public GatewayDb(DbContextOptions<GatewayDb> options) : base(options) { }

    public DbSet<UserEntity> Users => Set<UserEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Environment.GetEnvironmentVariable("ORACLE_SCHEMA") ?? "PROCESSCHECKER");

        b.Entity<UserEntity>(e =>
        {
            e.ToTable("PC_USERS");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.PasswordHash).HasMaxLength(300);
            e.Property(x => x.Role).HasMaxLength(20);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.AuthSource).HasColumnName("AUTH_SOURCE").HasMaxLength(10);
        });
    }
}