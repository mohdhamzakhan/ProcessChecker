using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace EngineService;

public class TaskMailer
{
    private static readonly Regex GroupToken = new(@"^group:[a-z0-9_.\- $]{1,64}$", RegexOptions.Compiled);
    private readonly NotificationClient _mail;
    private readonly IdentityResolver _ids;
    private readonly IConfiguration _cfg;
    private readonly ILogger<TaskMailer> _log;

    public TaskMailer(NotificationClient mail, IdentityResolver ids, IConfiguration cfg, ILogger<TaskMailer> log)
    { _mail = mail; _ids = ids; _cfg = cfg; _log = log; }

    /// Emails and group:name entries. Anything else is dropped.
    public static List<string> Parse(IEnumerable<string?> raw)
    {
        var list = new List<string>();
        foreach (var item in raw)
            foreach (var part in (item ?? "").Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var e = part.ToLowerInvariant();
                var ok = GroupToken.IsMatch(e)
                    || (MailAddress.TryCreate(e, out var a) && a.Address == e && e[(e.IndexOf('@') + 1)..].Contains('.'));
                if (ok && !list.Contains(e)) list.Add(e);
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

    private string Link(ProcessInstance inst) => $"{_cfg["Web:BaseUrl"]?.TrimEnd('/')}/instances/{inst.Id}";
    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");

    /// Called when a Form or Upload step becomes active.
    public async Task<StepResult> StartAssignedAsync(StepContext ctx, CancellationToken ct)
    {
        if (ctx.Def.Assignees.Count == 0)
        {
            ctx.Step.Assignees = null;
            return StepResult.Wait();
        }

        var list = Parse(ctx.Def.Assignees.Select(a => ctx.Data.Render(a)));
        if (list.Count == 0)
            return StepResult.Fail("Nobody could be assigned: the assignee fields have no valid email address or group.");

        ctx.Step.Assignees = Join(list);
        var mailError = await NotifyAsync(ctx.Instance, ctx.Step, list, ctx.Instance.StartedBy, ct);
        return StepResult.Wait(new { assignees = list, notifyError = mailError });
    }

    /// Best effort: a failed email never blocks the request. Returns the error text or null.
    public async Task<string?> NotifyAsync(ProcessInstance inst, StepInstance step, IEnumerable<string> to, string? by, CancellationToken ct)
    {
        try
        {
            var emails = await _ids.ToEmailsAsync(to, ct);
            if (emails.Count == 0) return null;
            var body =
                "<p>A task has been assigned to you.</p>" +
                $"<p><b>{Enc(inst.Title)}</b><br/>Step: {Enc(step.StepName)}" +
                (step.DueAt == null ? "" : $"<br/>Due: {step.DueAt:yyyy-MM-dd HH:mm} UTC") +
                (string.IsNullOrWhiteSpace(by) ? "" : $"<br/>Assigned by: {Enc(by)}") + "</p>" +
                $"<p><a href=\"{Link(inst)}\">Open the task</a></p>";
            await _mail.SendEmailAsync(emails, $"Task assigned: {inst.Title}", body, true, ct);
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
        try
        {
            var emails = await _ids.ToEmailsAsync(to, ct);
            if (emails.Count == 0) return;
            var body =
                "<p>You have been added as an observer. You can follow this request but not act on it.</p>" +
                $"<p><b>{Enc(inst.Title)}</b>" + (string.IsNullOrWhiteSpace(by) ? "" : $"<br/>Added by: {Enc(by)}") + "</p>" +
                $"<p><a href=\"{Link(inst)}\">Open the request</a></p>";
            await _mail.SendEmailAsync(emails, $"You are observing: {inst.Title}", body, true, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { _log.LogWarning("Observer email failed for {Id}: {Msg}", inst.Id, ex.Message); }
    }

    /// Returns the mail error, or null when sent (or when nobody could be found, so it is not retried forever).
    public async Task<string?> SendReminderAsync(ProcessInstance inst, StepInstance step, IEnumerable<string> who,
        bool overdue, CancellationToken ct)
    {
        try
        {
            var emails = await _ids.ToEmailsAsync(who, ct);
            if (emails.Count == 0) return null;
            var body =
                $"<p>{(overdue ? "This task is <b>overdue</b>." : "This task is still waiting for you.")}</p>" +
                $"<p><b>{Enc(inst.Title)}</b><br/>Step: {Enc(step.StepName)}" +
                (step.DueAt == null ? "" : $"<br/>Due: {step.DueAt:yyyy-MM-dd HH:mm} UTC") + "</p>" +
                $"<p><a href=\"{Link(inst)}\">Open the task</a></p>";
            await _mail.SendEmailAsync(emails, (overdue ? "Overdue: " : "Reminder: ") + inst.Title, body, true, ct);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning("Reminder email failed for {Id}: {Msg}", inst.Id, ex.Message);
            return ex.Message;
        }
    }
}