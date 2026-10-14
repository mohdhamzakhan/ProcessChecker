namespace ProcessChecker.Contracts;

public static class StepTypes
{
    public const string Approval = "Approval";   // email approvers, wait for approve/reject
    public const string Form = "Form";       // dynamic form built from Fields
    public const string Dispatch = "Dispatch";   // send collected data to a supplier/email
    public const string Upload = "Upload";     // user uploads a confirmation file
    public const string DataFetch = "DataFetch";  // query a named database connection
    public const string Notify = "Notify";     // send an email notification
    public const string Link = "Link";   // build a link to a file or page
    public const string Decision = "Decision";   // if / else with two lanes
    public const string End = "End";   // closes the request on this path

    public static readonly string[] All =
        { Approval, Form, Dispatch, Upload, DataFetch, Notify, Link  };
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
    public string Key { get; set; } = "";          // e.g. "business-travel"
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public TriggerDefinition Trigger { get; set; } = new();
    public List<StepDefinition> Steps { get; set; } = new();
    public List<string> Observers { get; set; } = new();   // emails or {{placeholders}}, can only watch
    public List<string> Requesters { get; set; } = new();   // usernames/emails or {{placeholders}}: who the request is for
    public List<LinkDefinition> Links { get; set; } = new();
}

public class LinkDefinition
{
    public string Label { get; set; } = "";
    public string Url { get; set; } = "";     // template, e.g. https://server/.../{{id}}?mid={{id+1}}
}

public class TriggerDefinition
{
    public string Type { get; set; } = TriggerTypes.Manual;
    public string? Connection { get; set; }        // named connection (DatabasePoll)
    public string? Query { get; set; }             // SQL that returns new entries
    public string? KeyColumn { get; set; }
    public int EveryMinutes { get; set; } = 5;
    public string? Schema { get; set; }
}

public class StepDefinition
{
    public string Key { get; set; } = "";          // unique inside the process
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";

    // Approval
    public List<string> Approvers { get; set; } = new();

    // Form
    public List<FieldDefinition> Fields { get; set; } = new();
    public bool AllowUpload { get; set; }

    // Dispatch / Notify
    public string? SendTo { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }              // supports {{field}} placeholders

    // DataFetch
    public string? Connection { get; set; }
    public string? Query { get; set; }
    public string? Schema { get; set; }
    public string? Url { get; set; }         // template, supports {{field}} and {{field+1}}
    public string? LinkLabel { get; set; }   // text shown for the link
    public List<string> Assignees { get; set; } = new();   // Form/Upload: who may do it (empty = anyone)
    public RunIfDefinition? RunIf { get; set; }    // null or no conditions: the step always runs
    public RunIfDefinition? Condition { get; set; }  // Decision: the "if"
    public BranchDefinition? Branch { get; set; }    // set when the step sits in a lane of a Decision
    public string? Outcome { get; set; }   // End: Completed | Rejected | Cancelled
    public string? Reason { get; set; }    // End: text shown on the request, may contain {{placeholders}}
}
public class BranchDefinition
{
    public string Of { get; set; } = "";       // key of the Decision step this step belongs to
    public string Path { get; set; } = "then"; // then | else
}
public class FieldDefinition
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string Type { get; set; } = "text";
    public bool Required { get; set; }
    public List<string> Options { get; set; } = new();   // for "select"
}
public class ConditionDefinition
{
    public string Field { get; set; } = "";
    public string Op { get; set; } = "eq";     // eq ne gt gte lt lte contains notcontains in notin empty notempty
    public string? Value { get; set; }         // may contain {{placeholders}}
}

public class RunIfDefinition
{
    public string Match { get; set; } = "all"; // all | any
    public List<ConditionDefinition> Conditions { get; set; } = new();
}


