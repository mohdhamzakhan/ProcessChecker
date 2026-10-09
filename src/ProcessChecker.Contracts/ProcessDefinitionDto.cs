namespace ProcessChecker.Contracts;

public static class StepTypes
{
    public const string Approval = "Approval";   // email approvers, wait for approve/reject
    public const string Form = "Form";       // dynamic form built from Fields
    public const string Dispatch = "Dispatch";   // send collected data to a supplier/email
    public const string Upload = "Upload";     // user uploads a confirmation file
    public const string DataFetch = "DataFetch";  // query a named database connection
    public const string Notify = "Notify";     // send an email notification

    public static readonly string[] All =
        { Approval, Form, Dispatch, Upload, DataFetch, Notify };
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
}

public class TriggerDefinition
{
    public string Type { get; set; } = TriggerTypes.Manual;
    public string? Connection { get; set; }        // named connection (DatabasePoll)
    public string? Query { get; set; }             // SQL that returns new entries
    public string? KeyColumn { get; set; }
    public int EveryMinutes { get; set; } = 5;
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
}

public class FieldDefinition
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string Type { get; set; } = "text";
    public bool Required { get; set; }
    public List<string> Options { get; set; } = new();   // for "select"
}