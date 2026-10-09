using Microsoft.EntityFrameworkCore;

namespace EngineService;

public class EngineDb : DbContext
{
    public EngineDb(DbContextOptions<EngineDb> options) : base(options) { }

    public DbSet<ProcessInstance> Instances => Set<ProcessInstance>();
    public DbSet<StepInstance> Steps => Set<StepInstance>();
    public DbSet<StepApproval> Approvals => Set<StepApproval>();
    public DbSet<TriggerSeen> TriggerSeenRows => Set<TriggerSeen>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Environment.GetEnvironmentVariable("ORACLE_SCHEMA") ?? "PROCESSCHECKER");

        b.Entity<ProcessInstance>(e =>
        {
            e.ToTable("PC_INSTANCES");
            e.Property(x => x.Id).ValueGeneratedNever();   // ids are created in code
            e.Property(x => x.ProcessKey).HasMaxLength(100);
            e.Property(x => x.ProcessName).HasMaxLength(200);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.StartedBy).HasMaxLength(200);
            e.Property(x => x.SourceKey).HasMaxLength(200);
            e.Property(x => x.DataJson).HasColumnType("CLOB");
            e.Property(x => x.DefinitionJson).HasColumnType("CLOB");
            e.Property(x => x.RowVer).IsConcurrencyToken();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.ProcessKey);
            e.HasMany(x => x.Steps).WithOne(s => s.Instance)
                .HasForeignKey(s => s.InstanceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<StepInstance>(e =>
        {
            e.ToTable("PC_STEPS");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.StepKey).HasMaxLength(100);
            e.Property(x => x.StepName).HasMaxLength(200);
            e.Property(x => x.StepType).HasMaxLength(30);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.OutputJson).HasColumnType("CLOB");
            e.Property(x => x.Error).HasMaxLength(1000);
            e.Property(x => x.ActionBy).HasMaxLength(200);
            e.HasIndex(x => x.Status);
            e.HasMany(x => x.Approvals).WithOne(a => a.Step)
                .HasForeignKey(a => a.StepInstanceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<StepApproval>(e =>
        {
            e.ToTable("PC_APPROVALS");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Approver).HasMaxLength(200);
            e.Property(x => x.Decision).HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasIndex(x => x.Approver);
        });

        b.Entity<TriggerSeen>(e =>
        {
            e.ToTable("PC_TRIGGER_SEEN");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ProcessKey).HasMaxLength(100);
            e.Property(x => x.SourceKey).HasMaxLength(200);
            e.HasIndex(x => new { x.ProcessKey, x.SourceKey }).IsUnique();
        });
    }
}