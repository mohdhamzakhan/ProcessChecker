using Microsoft.EntityFrameworkCore;

namespace DocumentService;

public class DocumentDb : DbContext
{
    public DocumentDb(DbContextOptions<DocumentDb> options) : base(options) { }

    public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Environment.GetEnvironmentVariable("ORACLE_SCHEMA") ?? "PROCESSCHECKER");

        b.Entity<DocumentEntity>(e =>
        {
            e.ToTable("PC_DOCUMENTS");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.FileName).HasMaxLength(200);
            e.Property(x => x.StoredPath).HasMaxLength(300);
            e.Property(x => x.ContentType).HasMaxLength(150);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.StepKey).HasMaxLength(100);
            e.Property(x => x.UploadedBy).HasMaxLength(200);
            e.HasIndex(x => x.InstanceId);
        });
    }
}