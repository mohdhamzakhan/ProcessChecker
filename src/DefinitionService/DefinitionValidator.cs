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
    @"^([^\s@;,{}]+@[^\s@;,{}]+\.[^\s@;,{}]+|\{\{\s*[\w.\-]+\s*\}\})$", RegexOptions.Compiled);

    private static readonly Regex LinkUrlStart =
    new(@"^https?://[^/?#{}\s]+(?=[/?#]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

            switch (s.Type)
            {
                case StepTypes.Approval:
                    if (s.Approvers.Count == 0)
                        errors.Add($"{p}: at least one approver is required.");
                    break;

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