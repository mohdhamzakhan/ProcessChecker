using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProcessChecker.Contracts;

namespace EngineService;

public enum StepOutcome { Waiting, Completed, Rejected, Failed }

public record StepResult(StepOutcome Outcome, object? Output = null, string? Error = null)
{
    public static StepResult Wait(object? output = null) => new(StepOutcome.Waiting, output);
    public static StepResult Done(object? output = null) => new(StepOutcome.Completed, output);
    public static StepResult Reject(object? output = null) => new(StepOutcome.Rejected, output);
    public static StepResult Fail(string error) => new(StepOutcome.Failed, null, error);
}

public class StepContext
{
    public required ProcessInstance Instance { get; init; }
    public required StepInstance Step { get; init; }
    public required StepDefinition Def { get; init; }
    public required DataBag Data { get; init; }
}

/// One class per step type. To add a new step type: implement this and register it in Program.cs.
public interface IStepHandler
{
    string Type { get; }

    /// Runs when the step becomes active. Return Wait for steps that need a person.
    Task<StepResult> StartAsync(StepContext ctx, CancellationToken ct);

    /// Runs when a person acts on a waiting step (approve, reject, submit, upload).
    Task<StepResult> ActAsync(StepContext ctx, StepAction action, CancellationToken ct) =>
        throw new EngineException(400, $"A {Type} step does not accept '{action.Name}'.");
}

// ------------------------------------------------------------ Approval
public class ApprovalHandler : IStepHandler
{
    private readonly NotificationClient _mail;
    private readonly IConfiguration _cfg;
    private readonly ILogger<ApprovalHandler> _log;

    public ApprovalHandler(NotificationClient mail, IConfiguration cfg, ILogger<ApprovalHandler> log)
    {
        _mail = mail; _cfg = cfg; _log = log;
    }

    public string Type => StepTypes.Approval;

    public async Task<StepResult> StartAsync(StepContext ctx, CancellationToken ct)
    {
        var approvers = ctx.Def.Approvers
            .Select(a => ctx.Data.Render(a).Trim().ToLowerInvariant())
            .Where(a => a.Length > 0)
            .Distinct()
            .ToList();

        if (approvers.Count == 0)
            return StepResult.Fail("No approvers could be resolved from the definition/data.");

        foreach (var a in approvers)
            ctx.Step.Approvals.Add(new StepApproval { StepInstanceId = ctx.Step.Id, Approver = a });

        string? notifyError = null;
        try
        {
            var link = $"{_cfg["Web:BaseUrl"]?.TrimEnd('/')}/instances/{ctx.Instance.Id}";
            var body =
                $"<p>Your approval is needed.</p>" +
                $"<p><b>{WebUtility.HtmlEncode(ctx.Instance.Title)}</b><br/>" +
                $"Step: {WebUtility.HtmlEncode(ctx.Step.StepName)}</p>" +
                $"<p><a href=\"{link}\">Open the request</a></p>";
            await _mail.SendEmailAsync(approvers, $"Approval needed: {ctx.Instance.Title}", body, true, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notifyError = ex.Message;      // do not block the process, approvers can still act in the UI
            _log.LogWarning("Approval email failed for {Id}: {Msg}", ctx.Instance.Id, ex.Message);
        }

        return StepResult.Wait(new { approvers, notifyError });
    }

    public Task<StepResult> ActAsync(StepContext ctx, StepAction a, CancellationToken ct)
    {
        if (a.Name is not ("approve" or "reject"))
            throw new EngineException(400, "Use 'approve' or 'reject'.");

        var by = a.By?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(by))
            throw new EngineException(400, "'by' (your email) is required.");

        var row = ctx.Step.Approvals.FirstOrDefault(x => x.Approver == by)
                  ?? throw new EngineException(403, "You are not an approver for this step.");
        if (row.Decision != null)
            throw new EngineException(409, "You have already decided on this step.");

        row.Decision = a.Name == "approve" ? "Approved" : "Rejected";
        row.Note = a.Note;
        row.DecidedAt = DateTime.UtcNow;
        ctx.Step.ActionBy = by;

        ctx.Data.Set($"{ctx.Step.StepKey}.decision", row.Decision);
        ctx.Data.Set($"{ctx.Step.StepKey}.approver", by);

        var output = new { decision = row.Decision, by, note = a.Note };
        // Rule: ANY one approver approves = step done; ANY rejection = process rejected
        return Task.FromResult(a.Name == "approve" ? StepResult.Done(output) : StepResult.Reject(output));
    }
}

// ------------------------------------------------------------ Form
public class FormHandler : IStepHandler
{
    public string Type => StepTypes.Form;

    public Task<StepResult> StartAsync(StepContext ctx, CancellationToken ct) =>
        Task.FromResult(StepResult.Wait());

    public Task<StepResult> ActAsync(StepContext ctx, StepAction a, CancellationToken ct)
    {
        if (a.Name != "submit")
            throw new EngineException(400, "Use 'submit' for a Form step.");

        var errors = new List<string>();
        var clean = new Dictionary<string, object?>();
        var values = new Dictionary<string, JsonElement>(
            a.Values ?? new Dictionary<string, JsonElement>(), StringComparer.OrdinalIgnoreCase);

        foreach (var f in ctx.Def.Fields)
        {
            var label = string.IsNullOrWhiteSpace(f.Label) ? f.Name : f.Label;
            values.TryGetValue(f.Name, out var el);
            var text = el.ValueKind == JsonValueKind.Undefined ? "" : DataBag.Text(el).Trim();

            if (f.Type == "checkbox")
            {
                var ticked = text.ToLowerInvariant() is "true" or "1" or "yes" or "on";
                if (f.Required && !ticked) errors.Add($"{label} must be ticked.");
                clean[f.Name] = ticked;
                continue;
            }

            if (text.Length == 0)
            {
                if (f.Required) errors.Add($"{label} is required.");
                else clean[f.Name] = "";
                continue;
            }

            switch (f.Type)
            {
                case "number":
                    if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var num))
                        clean[f.Name] = num;
                    else errors.Add($"{label} must be a number.");
                    break;

                case "date":
                    if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                        clean[f.Name] = dt.ToString("yyyy-MM-dd");
                    else errors.Add($"{label} must be a valid date.");
                    break;

                case "select":
                    var match = f.Options.FirstOrDefault(o => o.Equals(text, StringComparison.OrdinalIgnoreCase));
                    if (match == null) errors.Add($"{label} must be one of: {string.Join(", ", f.Options)}.");
                    else clean[f.Name] = match;
                    break;

                default:
                    clean[f.Name] = text;
                    break;
            }
        }

        if (a.Files is { Count: > 0 })
        {
            if (!ctx.Def.AllowUpload)
                errors.Add("This form does not accept attachments.");
            else if (a.Files.Any(x => string.IsNullOrWhiteSpace(x.FileId)))
                errors.Add("Every attachment needs a fileId.");
        }

        if (errors.Count > 0)
            throw new EngineException(400, "Form validation failed.", errors);

        foreach (var (name, value) in clean)
        {
            ctx.Data.Set(name, value);
            ctx.Data.Set($"{ctx.Step.StepKey}.{name}", value);
        }
        ctx.Step.ActionBy = a.By;

        return Task.FromResult(StepResult.Done(new { values = clean, files = a.Files }));
    }
}

// ------------------------------------------------------------ Upload
public class UploadHandler : IStepHandler
{
    public string Type => StepTypes.Upload;

    public Task<StepResult> StartAsync(StepContext ctx, CancellationToken ct) =>
        Task.FromResult(StepResult.Wait());

    public Task<StepResult> ActAsync(StepContext ctx, StepAction a, CancellationToken ct)
    {
        if (a.Name != "upload")
            throw new EngineException(400, "Use 'upload' for an Upload step.");
        if (a.Files == null || a.Files.Count == 0 || a.Files.Any(f => string.IsNullOrWhiteSpace(f.FileId)))
            throw new EngineException(400, "At least one file (with fileId) is required.");

        ctx.Step.ActionBy = a.By;
        ctx.Data.Set($"{ctx.Step.StepKey}.file", a.Files[0].FileName ?? a.Files[0].FileId);
        return Task.FromResult(StepResult.Done(new { files = a.Files }));
    }
}

// ------------------------------------------------------------ Dispatch + Notify (email)
public abstract class EmailStepHandler : IStepHandler
{
    private readonly NotificationClient _mail;
    protected EmailStepHandler(NotificationClient mail) => _mail = mail;

    public abstract string Type { get; }
    protected abstract string DefaultBody(StepContext ctx);

    public async Task<StepResult> StartAsync(StepContext ctx, CancellationToken ct)
    {
        var to = ctx.Data.Render(ctx.Def.SendTo)
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (to.Count == 0)
            return StepResult.Fail("SendTo resolved to no recipients.");

        var subject = ctx.Data.Render(
            string.IsNullOrWhiteSpace(ctx.Def.Subject) ? ctx.Def.Name : ctx.Def.Subject);

        string body;
        bool isHtml;
        if (string.IsNullOrWhiteSpace(ctx.Def.Body)) { body = DefaultBody(ctx); isHtml = true; }
        else { body = ctx.Data.Render(ctx.Def.Body); isHtml = false; }

        try { await _mail.SendEmailAsync(to, subject, body, isHtml, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return StepResult.Fail(ex.Message);
        }
        return StepResult.Done(new { to, subject });
    }
}

public class DispatchHandler : EmailStepHandler
{
    public DispatchHandler(NotificationClient mail) : base(mail) { }
    public override string Type => StepTypes.Dispatch;

    // No body in the definition: send everything collected so far as a table
    protected override string DefaultBody(StepContext ctx)
    {
        var sb = new StringBuilder();
        sb.Append($"<p>Request: <b>{WebUtility.HtmlEncode(ctx.Instance.Title)}</b></p>");
        sb.Append("<table border=\"1\" cellpadding=\"6\" cellspacing=\"0\">");
        foreach (var kv in ctx.Data.All.Where(k => !k.Key.Contains('.')).OrderBy(k => k.Key))
            sb.Append($"<tr><td>{WebUtility.HtmlEncode(kv.Key)}</td>" +
                      $"<td>{WebUtility.HtmlEncode(DataBag.Text(kv.Value))}</td></tr>");
        sb.Append("</table>");
        return sb.ToString();
    }
}

public class NotifyHandler : EmailStepHandler
{
    public NotifyHandler(NotificationClient mail) : base(mail) { }
    public override string Type => StepTypes.Notify;

    protected override string DefaultBody(StepContext ctx) =>
        $"<p>Update on <b>{WebUtility.HtmlEncode(ctx.Instance.Title)}</b>: " +
        $"{WebUtility.HtmlEncode(ctx.Step.StepName)}.</p>";
}

// ------------------------------------------------------------ DataFetch
public class DataFetchHandler : IStepHandler
{
    private static readonly Regex Literal = new("'(?:[^']|'')*'", RegexOptions.Compiled);
    private static readonly Regex Param = new(@"(?<!:):([A-Za-z_]\w*)", RegexOptions.Compiled);

    private readonly ConnectorClient _conn;
    public DataFetchHandler(ConnectorClient conn) => _conn = conn;

    public string Type => StepTypes.DataFetch;

    public async Task<StepResult> StartAsync(StepContext ctx, CancellationToken ct)
    {
        var query = ctx.Def.Query ?? "";

        // :name tokens in the query are filled from the process data as bind parameters
        var names = Param.Matches(Literal.Replace(query, "''"))
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ps = new Dictionary<string, JsonElement>();
        foreach (var n in names)
        {
            var v = ctx.Data.Get(n);
            if (v == null) return StepResult.Fail($"No value for :{n} in the process data.");
            ps[n] = v.Value;
        }

        QueryOutput res;
        try
        {
            res = await _conn.QueryAsync(ctx.Def.Connection!, query, ps.Count > 0 ? ps : null, 200, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return StepResult.Fail(ex.Message);
        }

        // First row becomes available to later steps as {{column}} and {{stepKey.column}}
        if (res.Rows.Count > 0)
            foreach (var (col, val) in res.Rows[0])
            {
                ctx.Data.SetElement(col, val);
                ctx.Data.SetElement($"{ctx.Step.StepKey}.{col}", val);
            }

        return StepResult.Done(new { count = res.Rows.Count, truncated = res.Truncated, rows = res.Rows.Take(50) });
    }
}