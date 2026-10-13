using System.Net;
using System.Net.Mail;

namespace EngineService;

public class TaskMailer
{
    private readonly NotificationClient _mail;
    private readonly IConfiguration _cfg;
    private readonly ILogger<TaskMailer> _log;

    public TaskMailer(NotificationClient mail, IConfiguration cfg, ILogger<TaskMailer> log)
    {
        _mail = mail; _cfg = cfg; _log = log;
    }

    public static List<string> Parse(IEnumerable<string?> raw)
    {
        var list = new List<string>();
        foreach (var item in raw)
            foreach (var part in (item ?? "").Split(new[] { ';', ',' },
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var e = part.ToLowerInvariant();
                if (MailAddress.TryCreate(e, out var a) && a.Address == e
                    && e[(e.IndexOf('@') + 1)..].Contains('.') && !list.Contains(e))
                    list.Add(e);
            }
        return list;
    }

    public static string? Join(IEnumerable<string> emails)
    {
        var l = emails.ToList();
        return l.Count == 0 ? null : ";" + string.Join(";", l) + ";";
    }

    public static List<string> Split(string? stored) =>
        (stored ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();

    /// Called when a Form or Upload step becomes active.
    public async Task<StepResult> StartAssignedAsync(StepContext ctx, CancellationToken ct)
    {
        if (ctx.Def.Assignees.Count == 0)
        {
            ctx.Step.Assignees = null;                      // anyone may do it
            return StepResult.Wait();
        }

        var list = Parse(ctx.Def.Assignees.Select(a => ctx.Data.Render(a)));
        if (list.Count == 0)
            return StepResult.Fail("Nobody could be assigned: the assignee fields have no valid email address.");

        ctx.Step.Assignees = Join(list);
        var mailError = await NotifyAsync(ctx.Instance, ctx.Step, list, ctx.Instance.StartedBy, ct);
        return StepResult.Wait(new { assignees = list, notifyError = mailError });
    }

    /// Best effort: a failed email never blocks the request. Returns the error text or null.
    public async Task<string?> NotifyAsync(
        ProcessInstance inst, StepInstance step, IEnumerable<string> to, string? by, CancellationToken ct)
    {
        var list = to.ToList();
        if (list.Count == 0) return null;
        try
        {
            var link = $"{_cfg["Web:BaseUrl"]?.TrimEnd('/')}/instances/{inst.Id}";
            var body =
                "<p>A task has been assigned to you.</p>" +
                $"<p><b>{WebUtility.HtmlEncode(inst.Title)}</b><br/>Step: {WebUtility.HtmlEncode(step.StepName)}" +
                (string.IsNullOrWhiteSpace(by) ? "" : $"<br/>Assigned by: {WebUtility.HtmlEncode(by)}") + "</p>" +
                $"<p><a href=\"{link}\">Open the task</a></p>";
            await _mail.SendEmailAsync(list, $"Task assigned: {inst.Title}", body, true, ct);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning("Task email failed for {Id}: {Msg}", inst.Id, ex.Message);
            return ex.Message;
        }
    }
    public async Task NotifyObserversAsync(ProcessInstance inst, IEnumerable<string> to, string? by, CancellationToken ct)
    {
        var list = to.ToList();
        if (list.Count == 0) return;
        try
        {
            var link = $"{_cfg["Web:BaseUrl"]?.TrimEnd('/')}/instances/{inst.Id}";
            var body =
                "<p>You have been added as an observer. You can follow this request but not act on it.</p>" +
                $"<p><b>{WebUtility.HtmlEncode(inst.Title)}</b>" +
                (string.IsNullOrWhiteSpace(by) ? "" : $"<br/>Added by: {WebUtility.HtmlEncode(by)}") + "</p>" +
                $"<p><a href=\"{link}\">Open the request</a></p>";
            await _mail.SendEmailAsync(list, $"You are observing: {inst.Title}", body, true, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning("Observer email failed for {Id}: {Msg}", inst.Id, ex.Message);
        }
    }
}