using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace EngineService;

public record DigestItem(ProcessInstance Inst, StepInstance Step, bool Overdue);

public class TaskMailer
{
    private static readonly Regex GroupToken = new(@"^group:[a-z0-9_.\- $]{1,64}$", RegexOptions.Compiled);
    private readonly NotificationClient _mail;
    private readonly IdentityResolver _ids;
    private readonly AwayService _away;
    private readonly IConfiguration _cfg;
    private readonly ILogger<TaskMailer> _log;

    public TaskMailer(NotificationClient mail, IdentityResolver ids, AwayService away, IConfiguration cfg, ILogger<TaskMailer> log)
    { _mail = mail; _ids = ids; _away = away; _cfg = cfg; _log = log; }

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

        var parsed = Parse(ctx.Def.Assignees.Select(a => ctx.Data.Render(a)));
        if (parsed.Count == 0)
            return StepResult.Fail("Nobody could be assigned: the assignee fields have no valid email address or group.");

        var (list, awayNotes) = await _away.ApplyAsync(parsed, ct);
        ctx.Step.Assignees = Join(list);
        var mailError = await NotifyAsync(ctx.Instance, ctx.Step, list, ctx.Instance.StartedBy, ct);
        return StepResult.Wait(new { assignees = list, notifyError = mailError, away = awayNotes });
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
    public async Task SendEscalationAsync(ProcessInstance inst, StepInstance step, string action,
    IReadOnlyList<string> targets, IReadOnlyList<string> originals, string? extra, CancellationToken ct)
    {
        try
        {
            var due = step.DueAt == null ? "" : $"Due: {step.DueAt:yyyy-MM-dd HH:mm} UTC<br/>";
            var note = string.IsNullOrWhiteSpace(extra) ? "" : $"<p>{Enc(extra)}</p>";
            var link = $"<p><a href=\"{Link(inst)}\">Open the request</a></p>";
            var head = $"<p><b>{Enc(inst.Title)}</b><br/>Step: {Enc(step.StepName)}<br/>{due}</p>";

            var toTargets = await _ids.ToEmailsAsync(targets, ct);
            if (toTargets.Count > 0)
            {
                var intro = action == "forward"
                    ? "A task was not handled by its deadline and has been <b>forwarded to you</b>."
                    : "A task passed its deadline. You are being told because of the escalation rules.";
                await _mail.SendEmailAsync(toTargets, $"Escalated: {inst.Title}", $"<p>{intro}</p>{head}{note}{link}", true, ct);
            }

            var who = originals.ToList();
            if (!string.IsNullOrWhiteSpace(inst.StartedBy)) who.Add(inst.StartedBy!);
            var toOthers = (await _ids.ToEmailsAsync(who, ct)).Except(toTargets, StringComparer.OrdinalIgnoreCase).ToList();
            if (toOthers.Count > 0)
            {
                var text = action switch
                {
                    "forward" => "A task was not handled by its deadline and has been forwarded to someone else.",
                    "reject" => "A task was not handled by its deadline, so the request was <b>rejected automatically</b>.",
                    _ => "A task passed its deadline."
                };
                await _mail.SendEmailAsync(toOthers, $"Deadline passed: {inst.Title}", $"<p>{text}</p>{head}{link}", true, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { _log.LogWarning("Escalation email failed for {Id}: {Msg}", inst.Id, ex.Message); }
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

    /// One email listing all of a person's waiting tasks. `email` is already an address. Returns the error or null.
    public async Task<string?> SendDigestAsync(string email, IReadOnlyList<DigestItem> items, CancellationToken ct)
    {
        try
        {
            var ordered = items.OrderByDescending(i => i.Overdue).ThenBy(i => i.Step.DueAt ?? DateTime.MaxValue).ToList();
            var late = ordered.Count(i => i.Overdue);

            var sb = new StringBuilder();
            sb.Append($"<p>You have <b>{ordered.Count}</b> task{(ordered.Count == 1 ? "" : "s")} waiting" +
                      (late > 0 ? $", <b>{late} overdue</b>" : "") + ".</p>");
            sb.Append("<table cellpadding=\"6\" cellspacing=\"0\" border=\"1\" style=\"border-collapse:collapse\">");
            sb.Append("<tr><th align=\"left\">Request</th><th align=\"left\">Step</th><th align=\"left\">Due</th></tr>");
            foreach (var i in ordered)
            {
                var due = i.Step.DueAt == null ? "–" : $"{i.Step.DueAt:yyyy-MM-dd HH:mm} UTC" + (i.Overdue ? " <b>(overdue)</b>" : "");
                sb.Append($"<tr><td><a href=\"{Link(i.Inst)}\">{Enc(i.Inst.Title)}</a></td><td>{Enc(i.Step.StepName)}</td><td>{due}</td></tr>");
            }
            sb.Append("</table>");

            var subject = $"Reminder: {ordered.Count} task{(ordered.Count == 1 ? "" : "s")} waiting for you" +
                          (late > 0 ? $" ({late} overdue)" : "");
            await _mail.SendEmailAsync(new[] { email }, subject, sb.ToString(), true, ct);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning("Digest email to {Email} failed: {Msg}", email, ex.Message);
            return ex.Message;
        }
    }
}