using Microsoft.EntityFrameworkCore;

namespace EngineService;

public class EngineDb : DbContext
{
    public EngineDb(DbContextOptions<EngineDb> options) : base(options) { }

    public DbSet<ProcessInstance> Instances => Set<ProcessInstance>();
    public DbSet<StepInstance> Steps => Set<StepInstance>();
    public DbSet<StepApproval> Approvals => Set<StepApproval>();
    public DbSet<TriggerSeen> TriggerSeenRows => Set<TriggerSeen>();
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<DashboardEntity> Dashboards => Set<DashboardEntity>();
    public DbSet<AwayEntity> Away { get; set; } = null!;

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
            e.Property(x => x.Observers).HasColumnName("OBSERVERS").HasMaxLength(2000);
            e.Property(x => x.Requester).HasColumnName("REQUESTER").HasMaxLength(2000);
        });

        b.Entity<AwayEntity>(e =>
        {
            e.ToTable("PC_AWAY");
            e.Property(x => x.Id).HasColumnName("ID").ValueGeneratedNever();
            e.Property(x => x.Email).HasColumnName("EMAIL").HasMaxLength(200);
            e.Property(x => x.DelegateTo).HasColumnName("DELEGATE_TO").HasMaxLength(200);
            e.Property(x => x.FromAt).HasColumnName("FROM_AT");
            e.Property(x => x.ToAt).HasColumnName("TO_AT");
            e.Property(x => x.Active).HasColumnName("ACTIVE");
            e.Property(x => x.CreatedAt).HasColumnName("CREATED_AT");
            e.HasIndex(x => x.Email).IsUnique();
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
            e.Property(x => x.Assignees).HasColumnName("ASSIGNEES").HasMaxLength(2000);
            e.Property(x => x.DueAt).HasColumnName("DUE_AT");
            e.Property(x => x.RemindHours).HasColumnName("REMIND_HOURS");
            e.Property(x => x.LastReminderAt).HasColumnName("LAST_REMINDER_AT");
            e.Property(x => x.ReminderCount).HasColumnName("REMINDER_COUNT");
        });

        b.Entity<StepApproval>(e =>
        {
            e.ToTable("PC_APPROVALS");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Approver).HasMaxLength(200);
            e.Property(x => x.Decision).HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasIndex(x => x.Approver);
            e.Property(x => x.DecidedBy).HasColumnName("DECIDED_BY").HasMaxLength(200);
        });

        b.Entity<TriggerSeen>(e =>
        {
            e.ToTable("PC_TRIGGER_SEEN");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ProcessKey).HasMaxLength(100);
            e.Property(x => x.SourceKey).HasMaxLength(200);
            e.HasIndex(x => new { x.ProcessKey, x.SourceKey }).IsUnique();
        });

        b.Entity<Participant>(e =>
        {
            e.ToTable("PC_PARTICIPANTS");
            e.Property(x => x.Id).HasColumnName("ID").ValueGeneratedNever();
            e.Property(x => x.InstanceId).HasColumnName("INSTANCE_ID");
            e.Property(x => x.Email).HasColumnName("EMAIL").HasMaxLength(200);
            e.Property(x => x.Role).HasColumnName("ROLE_NAME").HasMaxLength(20);
            e.HasIndex(x => new { x.Email, x.InstanceId });
        });

        b.Entity<DashboardEntity>(e =>
        {
            e.ToTable("PC_DASHBOARDS");
            e.Property(x => x.Id).HasColumnName("ID").ValueGeneratedNever();
            e.Property(x => x.OwnerEmail).HasColumnName("OWNER_EMAIL").HasMaxLength(200);
            e.Property(x => x.Name).HasColumnName("NAME").HasMaxLength(200);
            e.Property(x => x.ConfigJson).HasColumnName("CONFIG_JSON").HasColumnType("CLOB");
            e.Property(x => x.CreatedAt).HasColumnName("CREATED_AT");
            e.Property(x => x.UpdatedAt).HasColumnName("UPDATED_AT");
            e.HasIndex(x => x.OwnerEmail);
        });
    }
}