namespace ProcessChecker.Contracts;

public static class StepTypes
{
    public const string Approval = "Approval";
    public const string Form = "Form";
    public const string Dispatch = "Dispatch";
    public const string Upload = "Upload";
    public const string DataFetch = "DataFetch";
    public const string Notify = "Notify";
    public const string Link = "Link";       // legacy, not offered in the designer
    public const string Decision = "Decision";   // if / else with two paths
    public const string End = "End";        // closes the request on this path
    public const string Parallel = "Parallel";   // several paths at the same time

    // every type must be listed here, otherwise a process using it is rejected on import
    public static readonly string[] All =
        { Approval, Form, Dispatch, Upload, DataFetch, Notify, Link, Decision, End, Parallel };
}

public static class FieldTypes
{
    public static readonly string[] All =
        { "text", "textarea", "number", "date", "select", "checkbox" };
}

public static class TriggerTypes
{
    public const string Manual = "Manual";
    public const string DatabasePoll = "DatabasePoll";
}

public class ProcessDefinitionDto
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public TriggerDefinition Trigger { get; set; } = new();
    public List<StepDefinition> Steps { get; set; } = new();
    public List<LinkDefinition> Links { get; set; } = new();
    public List<string> Observers { get; set; } = new();
    public List<string> Requesters { get; set; } = new();
    public List<NotifyRule> Notifications { get; set; } = new();
}

public class TriggerDefinition
{
    public string Type { get; set; } = TriggerTypes.Manual;
    public string? Connection { get; set; }
    public string? Schema { get; set; }
    public string? Query { get; set; }
    public string? KeyColumn { get; set; }
    public int EveryMinutes { get; set; } = 5;
}

public class StepDefinition
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";

    // Approval
    public List<string> Approvers { get; set; } = new();
    public bool RequireAll { get; set; }

    // Form / Upload
    public List<FieldDefinition> Fields { get; set; } = new();
    public bool AllowUpload { get; set; }
    public List<string> Assignees { get; set; } = new();

    // Dispatch / Notify
    public string? SendTo { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
    public bool AttachFiles { get; set; }
    public List<string> AttachFrom { get; set; } = new();

    // DataFetch
    public string? Connection { get; set; }
    public string? Schema { get; set; }
    public string? Query { get; set; }

    // Link (legacy)
    public string? Url { get; set; }
    public string? LinkLabel { get; set; }

    // conditions, paths and ending
    public RunIfDefinition? RunIf { get; set; }          // run this step only if...
    public RunIfDefinition? Condition { get; set; }      // Decision: the "if"
    public BranchDefinition? Branch { get; set; }        // set when the step sits in a path of a block
    public List<LaneDefinition> Lanes { get; set; } = new();   // Parallel: its paths
    public string? Outcome { get; set; }                 // End: Completed | Rejected | Cancelled
    public string? Reason { get; set; }

    // deadline, reminders, escalation
    public int? DueHours { get; set; }
    public int? RemindEveryHours { get; set; }
    public EscalationDefinition? Escalation { get; set; }
    public string? EmailStyle { get; set; }     // info | success | warning | alert
    public string? ButtonLabel { get; set; }    // e.g. "Open the request"; empty = no button
}

public class FieldDefinition
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string Type { get; set; } = "text";
    public bool Required { get; set; }
    public List<string> Options { get; set; } = new();
}

public class LinkDefinition
{
    public string Label { get; set; } = "";
    public string Url { get; set; } = "";
}

public class ConditionDefinition
{
    public string Field { get; set; } = "";
    public string Op { get; set; } = "eq";      // eq ne gt gte lt lte contains notcontains in notin empty notempty
    public string? Value { get; set; }
}

public class RunIfDefinition
{
    public string Match { get; set; } = "all";  // all | any
    public List<ConditionDefinition> Conditions { get; set; } = new();
}

public class BranchDefinition
{
    public string Of { get; set; } = "";        // key of the Decision or Parallel step
    public string Path { get; set; } = "then";  // then | else, or the id of a parallel path
}

public class LaneDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public class NotifyRule
{
    public string On { get; set; } = "";        // fileUploaded | requestEnded
    public bool ToRequester { get; set; }
    public bool ToStarter { get; set; }
    public List<string> To { get; set; } = new();
    public string? Message { get; set; }
}

public class EscalationDefinition
{
    public int AfterHours { get; set; }
    public string Action { get; set; } = "forward";   // forward | reject | notify
    public List<string> To { get; set; } = new();
    public string? Message { get; set; }
}