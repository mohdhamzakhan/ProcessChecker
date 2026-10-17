using System.Text.RegularExpressions;
using ProcessChecker.Contracts;

namespace DefinitionService;

public class DefinitionValidator
{
    private static readonly Regex KeyRegex = new("^[a-z0-9-]+$", RegexOptions.Compiled);
    private static readonly Regex SchemaRegex = new(@"^[A-Za-z][A-Za-z0-9_$#]{0,127}$", RegexOptions.Compiled);
    private static readonly Regex PlaceholderRegex = new(@"^\{\{\s*[\w.\-]+\s*\}\}$", RegexOptions.Compiled);
    private static readonly Regex UrlStart =
    new(@"^https?://[^/?#{}\s]+(?=[/?#]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AssigneeRegex = new(
        @"^([^\s@;,{}]+@[^\s@;,{}]+\.[^\s@;,{}]+|\{\{\s*[\w.\-]+\s*\}\}|group:[A-Za-z0-9_.\- $]{1,64})$",
        RegexOptions.Compiled);

    private static readonly Regex LinkUrlStart =
    new(@"^https?://[^/?#{}\s]+(?=[/?#]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] CondOps =
    { "eq", "ne", "gt", "gte", "lt", "lte", "contains", "notcontains", "in", "notin", "empty", "notempty" };
    private static readonly Regex CondField = new(@"^[\w.\-]+$", RegexOptions.Compiled);


    private static void CheckConditions(RunIfDefinition? r, string p, List<string> errors)
    {
        if (r is null || r.Conditions.Count == 0) return;

        if (r.Match is not ("all" or "any")) errors.Add($"{p}: the condition match must be 'all' or 'any'.");
        if (r.Conditions.Count > 10) errors.Add($"{p}: at most 10 conditions per step.");

        foreach (var c in r.Conditions)
        {
            if (string.IsNullOrWhiteSpace(c.Field) || !CondField.IsMatch(c.Field.Trim()))
                errors.Add($"{p}: a condition needs a valid field name.");
            if (!CondOps.Contains(c.Op))
                errors.Add($"{p}: unknown condition operator '{c.Op}'.");
            else if (c.Op is not ("empty" or "notempty") && string.IsNullOrWhiteSpace(c.Value))
                errors.Add($"{p}: the condition on '{c.Field}' needs a value.");
            if ((c.Value?.Length ?? 0) > 500) errors.Add($"{p}: a condition value is too long.");
        }
    }

    private static void CheckRunIf(StepDefinition s, string p, List<string> errors) =>
        CheckConditions(s.RunIf, p, errors);

    private static void CheckBranch(StepDefinition s, string p, List<StepDefinition> all, List<string> errors)
    {
        var b = s.Branch;
        if (b is null) return;

        var at = all.IndexOf(s);
        var parent = all.FindIndex(x => x.Key == b.Of);
        if (parent < 0 || all[parent].Type is not (StepTypes.Decision or StepTypes.Parallel))
        { errors.Add($"{p}: it belongs to a block that does not exist."); return; }
        if (parent >= at) errors.Add($"{p}: its block must come before it.");

        var owner = all[parent];
        var ok = owner.Type == StepTypes.Decision
            ? b.Path is "then" or "else"
            : owner.Lanes.Any(l => l.Id == b.Path);
        if (!ok) errors.Add($"{p}: it is in a path that does not exist.");
    }
    private static void CheckEscalation(StepDefinition s, string p, List<string> errors)
    {
        var e = s.Escalation;
        if (e is null) return;

        if (s.Type is not (StepTypes.Form or StepTypes.Upload or StepTypes.Approval))
        { errors.Add($"{p}: only Form, Upload and Approval steps can escalate."); return; }
        if (s.DueHours is not > 0) errors.Add($"{p}: escalation needs a deadline (set 'due after' first).");
        if (e.AfterHours is < 0 or > 8760) errors.Add($"{p}: escalate between 0 hours and 1 year after the deadline.");
        if (e.Action is not ("forward" or "reject" or "notify"))
            errors.Add($"{p}: the escalation action must be forward, reject or notify.");

        if (e.Action is "forward" or "notify")
        {
            if (e.To.Count == 0) errors.Add($"{p}: escalation needs at least one person.");
            foreach (var t in e.To)
                if (!AssigneeRegex.IsMatch(t.Trim()))
                    errors.Add(p + ": '" + t + "' is not an email address, a group or a {{field}} placeholder.");
        }
        if ((e.Message?.Length ?? 0) > 500) errors.Add($"{p}: the escalation message is too long (500 characters).");
    }
    private static void CheckSchedule(StepDefinition s, string p, List<string> errors)
    {
        if (s.DueHours is { } d && (d < 1 || d > 8760)) errors.Add($"{p}: the deadline must be between 1 hour and 1 year.");
        if (s.RemindEveryHours is { } r && (r < 1 || r > 720)) errors.Add($"{p}: the reminder interval must be between 1 hour and 30 days.");
    }

    private static void CheckNotify(ProcessDefinitionDto d, List<string> errors)
    {
        if (d.Notifications.Count > 10) errors.Add("At most 10 email rules are allowed.");
        foreach (var n in d.Notifications)
        {
            if (n.On is not ("fileUploaded" or "requestEnded")) errors.Add($"Unknown email rule '{n.On}'.");
            if (!n.ToRequester && !n.ToStarter && n.To.Count == 0) errors.Add($"The email rule '{n.On}' has nobody to send to.");
            foreach (var t in n.To)
                if (!AssigneeRegex.IsMatch(t.Trim()))
                    errors.Add("Email rule '" + n.On + "': '" + t + "' is not an email address, a group or a {{field}} placeholder.");
            if ((n.Message?.Length ?? 0) > 1000) errors.Add("An email rule message is too long (1000 characters).");
        }
    }

    private static void CheckAssignees(StepDefinition s, string p, List<string> errors)
    {
        foreach (var a in s.Assignees)
            if (!AssigneeRegex.IsMatch(a.Trim()))
                errors.Add(p + ": '" + a + "' is not an email address or a {{field}} placeholder.");
    }
    public List<string> Validate(ProcessDefinitionDto d)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(d.Key) || !KeyRegex.IsMatch(d.Key))
            errors.Add("Key is required and may only contain a-z, 0-9 and '-'.");
        if (string.IsNullOrWhiteSpace(d.Name))
            errors.Add("Name is required.");



        // Trigger
        if (d.Trigger.Type == TriggerTypes.DatabasePoll)
        {
            if (string.IsNullOrWhiteSpace(d.Trigger.Connection))
                errors.Add("Trigger: Connection is required for DatabasePoll.");
            if (string.IsNullOrWhiteSpace(d.Trigger.Query))
                errors.Add("Trigger: Query is required for DatabasePoll.");
            if (d.Trigger.EveryMinutes < 1)
                errors.Add("Trigger: EveryMinutes must be at least 1.");
        }
        else if (d.Trigger.Type != TriggerTypes.Manual)
        {
            errors.Add($"Trigger: unknown type '{d.Trigger.Type}'.");
        }

        // Steps
        if (d.Steps.Count == 0)
            errors.Add("At least one step is required.");

        var seenKeys = new HashSet<string>();
        foreach (var s in d.Steps)
        {
            var p = $"Step '{s.Key}'";

            if (string.IsNullOrWhiteSpace(s.Key) || !KeyRegex.IsMatch(s.Key))
                errors.Add($"{p}: key is required (a-z, 0-9, '-').");
            else if (!seenKeys.Add(s.Key))
                errors.Add($"{p}: duplicate step key.");

            if (string.IsNullOrWhiteSpace(s.Name))
                errors.Add($"{p}: name is required.");

            foreach (var o in d.Observers)
                if (!AssigneeRegex.IsMatch(o.Trim()))
                    errors.Add("Observer '" + o + "' is not an email address or a {{field}} placeholder.");

            foreach (var r in d.Requesters)
                if (!AssigneeRegex.IsMatch(r.Trim()))
                    errors.Add("Requester '" + r + "' must be an email address or a {{field}} placeholder.");
            if (d.Requesters.Count > 5) errors.Add("At most 5 requester entries are allowed.");

            foreach (var l in d.Links)
                if (!LinkUrlStart.IsMatch((l.Url ?? "").Trim()))
                    errors.Add("Link '" + l.Label + "': the address must start with http:// or https:// and a fixed server name.");

            if (!StepTypes.All.Contains(s.Type))
            {
                errors.Add($"{p}: unknown type '{s.Type}'.");
                continue;
            }

            // inside the "if (d.Trigger.Type == TriggerTypes.DatabasePoll)" block:
            if (CheckSchema(d.Trigger.Schema, false) is { } te) errors.Add("Trigger: " + te);

            // inside "case StepTypes.DataFetch:", after the existing checks:
            if (CheckSchema(s.Schema, true) is { } se) errors.Add($"{p}: {se}");
            CheckRunIf(s, p, errors);
            CheckBranch(s, p, d.Steps, errors);
            CheckNotify(d, errors);
            CheckSchedule(s, p, errors);
            CheckEscalation(s, p, errors);
            switch (s.Type)
            {
                case StepTypes.Approval:
                    if (s.Approvers.Count == 0)
                        errors.Add($"{p}: at least one approver is required.");
                    foreach (var a in s.Approvers)
                        if (!AssigneeRegex.IsMatch(a.Trim()))
                            errors.Add(p + ": '" + a + "' is not an email address, a group or a {{field}} placeholder.");
                    break;

                case StepTypes.Parallel:
                    {
                        if (s.Lanes.Count is < 2 or > 6) errors.Add($"{p}: a Parallel step needs 2 to 6 paths.");
                        var laneIds = new HashSet<string>();
                        foreach (var l in s.Lanes)
                        {
                            if (!Regex.IsMatch(l.Id ?? "", "^[a-z0-9]{1,12}$")) errors.Add($"{p}: a path has an invalid id.");
                            else if (!laneIds.Add(l.Id)) errors.Add($"{p}: two paths share the id '{l.Id}'.");
                            if (string.IsNullOrWhiteSpace(l.Name) || l.Name.Length > 60) errors.Add($"{p}: every path needs a name (up to 60 characters).");
                        }
                        break;
                    }

                case StepTypes.Form:
                    ValidateFields(s, p, errors);
                    CheckAssignees(s, p, errors);
                    break;

                case StepTypes.Upload:
                    CheckAssignees(s, p, errors);
                    break;

                case StepTypes.Dispatch:
                    if (string.IsNullOrWhiteSpace(s.SendTo))
                        errors.Add($"{p}: SendTo is required.");
                    break;

                case StepTypes.Notify:
                    if (string.IsNullOrWhiteSpace(s.SendTo))
                        errors.Add($"{p}: SendTo is required.");
                    if (string.IsNullOrWhiteSpace(s.Subject))
                        errors.Add($"{p}: Subject is required.");
                    break;

                case StepTypes.DataFetch:
                    if (string.IsNullOrWhiteSpace(s.Connection))
                        errors.Add($"{p}: Connection is required.");
                    if (string.IsNullOrWhiteSpace(s.Query))
                        errors.Add($"{p}: Query is required.");
                    break;

                case StepTypes.Link:
                    if (string.IsNullOrWhiteSpace(s.Url))
                        errors.Add($"{p}: Url is required.");
                    else if (!UrlStart.IsMatch(s.Url.Trim()))
                        errors.Add($"{p}: Url must start with http:// or https:// and a fixed server name (no placeholder in the server part).");
                    break;

                case StepTypes.Decision:
                    if (s.Condition is null || s.Condition.Conditions.Count == 0)
                        errors.Add($"{p}: an If / Else step needs at least one condition.");
                    else CheckConditions(s.Condition, p, errors);
                    break;
                case StepTypes.End:
                    if (s.Outcome is not (null or "Completed" or "Rejected" or "Cancelled"))
                        errors.Add($"{p}: the outcome must be Completed, Rejected or Cancelled.");
                    if ((s.Reason?.Length ?? 0) > 500)
                        errors.Add($"{p}: the reason is too long (500 characters).");
                    break;
            }
        }

        return errors;
    }

    private static string? CheckSchema(string? schema, bool allowPlaceholder)
    {
        if (string.IsNullOrWhiteSpace(schema)) return null;
        var s = schema.Trim();
        if (SchemaRegex.IsMatch(s)) return null;
        if (allowPlaceholder && PlaceholderRegex.IsMatch(s)) return null;
        return "schema '" + s + "' is not valid (letters, numbers, _ $ #" +
               (allowPlaceholder ? ", or a {{field}} placeholder" : "") + ").";
    }
    private static void ValidateFields(StepDefinition s, string p, List<string> errors)
    {
        if (s.Fields.Count == 0)
        {
            errors.Add($"{p}: a Form step needs at least one field.");
            return;
        }

        var names = new HashSet<string>();
        foreach (var f in s.Fields)
        {
            if (string.IsNullOrWhiteSpace(f.Name))
                errors.Add($"{p}: every field needs a name.");
            else if (!names.Add(f.Name))
                errors.Add($"{p}: duplicate field name '{f.Name}'.");

            if (!FieldTypes.All.Contains(f.Type))
                errors.Add($"{p}: field '{f.Name}' has unknown type '{f.Type}'.");

            if (f.Type == "select" && f.Options.Count == 0)
                errors.Add($"{p}: select field '{f.Name}' needs options.");
        }
    }
}