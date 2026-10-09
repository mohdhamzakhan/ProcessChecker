using System.Text.Json;
using ProcessChecker.Contracts;

namespace EngineService;

public static class InstanceStatus
{
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Rejected = "Rejected";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

public static class StepState
{
    public const string Pending = "Pending";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Rejected = "Rejected";
    public const string Failed = "Failed";
}

public class ProcessInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProcessKey { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public int ProcessVersion { get; set; }
    public string Title { get; set; } = "";
    public string Status { get; set; } = InstanceStatus.Running;
    public string? StartedBy { get; set; }
    public string? SourceKey { get; set; }
    public string DataJson { get; set; } = "{}";
    public string DefinitionJson { get; set; } = "";   // snapshot of the definition used
    public int RowVer { get; set; }                    // optimistic concurrency
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public List<StepInstance> Steps { get; set; } = new();
}

public class StepInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstanceId { get; set; }
    public ProcessInstance Instance { get; set; } = null!;
    public string StepKey { get; set; } = "";
    public string StepName { get; set; } = "";
    public string StepType { get; set; } = "";
    public int OrderNo { get; set; }
    public string Status { get; set; } = StepState.Pending;
    public string? OutputJson { get; set; }
    public string? Error { get; set; }
    public string? ActionBy { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<StepApproval> Approvals { get; set; } = new();
}

public class StepApproval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StepInstanceId { get; set; }
    public StepInstance Step { get; set; } = null!;
    public string Approver { get; set; } = "";         // lower-case email
    public string? Decision { get; set; }              // null = waiting, Approved, Rejected
    public string? Note { get; set; }
    public DateTime? DecidedAt { get; set; }
}

public class TriggerSeen
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProcessKey { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public Guid? InstanceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ---------- API / handler contracts ----------
public record FileRef(string FileId, string? FileName);
public record StartRequest(string ProcessKey, Dictionary<string, JsonElement>? Data, string? StartedBy, string? Title);
public record ActRequest(string? By, string? Note, Dictionary<string, JsonElement>? Values, List<FileRef>? Files);
public record StepAction(string Name, string? By, string? Note, Dictionary<string, JsonElement>? Values, List<FileRef>? Files);

public record DefinitionInfo(
    Guid Id, string Key, string Name, int Version, bool IsActive,
    DateTime CreatedAt, ProcessDefinitionDto Definition);

// ---------- Views returned to React ----------
public record ApprovalView(string Approver, string? Decision, string? Note, DateTime? DecidedAt);

public record StepView(
    Guid Id, string Key, string Name, string Type, int Order, string Status,
    string? ActionBy, DateTime? StartedAt, DateTime? CompletedAt, string? Error,
    JsonElement? Output, List<FieldDefinition>? Fields, bool? AllowUpload,
    List<ApprovalView>? Approvals);

public record InstanceSummary(
    Guid Id, string ProcessKey, string ProcessName, int ProcessVersion, string Title,
    string Status, string? StartedBy, DateTime CreatedAt, DateTime? CompletedAt,
    int TotalSteps, int CompletedSteps, string? CurrentStep);

public record InstanceDetail(
    InstanceSummary Summary, Dictionary<string, JsonElement> Data, List<StepView> Steps);

public record TaskItem(
    Guid InstanceId, string Title, string ProcessName,
    string StepKey, string StepName, string StepType, DateTime? Since);

public class EngineException : Exception
{
    public int Status { get; }
    public List<string> Errors { get; }

    public EngineException(int status, string message, List<string>? errors = null) : base(message)
    {
        Status = status;
        Errors = errors ?? new List<string>();
    }
}