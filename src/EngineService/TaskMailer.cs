using System.Net.Mail;
using System.Text.RegularExpressions;

namespace EngineService;

public record DigestItem(ProcessInstance Inst, StepInstance Step, bool Overdue);

public class TaskMailer
{
    private static readonly Regex GroupToken = new(@"^group:[a-z0-9_.\- $]{1,64}$", RegexOptions.Compiled);
    private readonly NotificationClient _mail;
    private readonly IdentityResolver _ids;
    private readonly AwayService _away;
    private readonly DisplayTime _time;
    private readonly IConfiguration _cfg;
    private readonly ILogger<TaskMailer> _log;

    public TaskMailer(NotificationClient mail, IdentityResolver ids, AwayService away, DisplayTime time,
        IConfiguration cfg, ILogger<TaskMailer> log)
    { _mail = mail; _ids = ids; _away = away; _time = time; _cfg = cfg; _log = log; }

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

    private string Base => (_cfg["Web:BaseUrl"] ?? "").TrimEnd('/');
    private string Link(ProcessInstance inst) => $"{Base}/instances/{inst.Id}";

    private List<MailDetail> Rows(ProcessInstance inst, StepInstance step, DateTime? due, string? by,
        string dueLabel = "Due", string byLabel = "Assigned by")
    {
        var l = new List<MailDetail> { new("Process", inst.ProcessName), new("Step", step.StepName) };
        if (due != null) l.Add(new(dueLabel, _time.Format(due)));
        if (!string.IsNullOrWhiteSpace(by)) l.Add(new(byLabel, by == "system:poll" ? "Started automatically" : by));
        return l;
    }

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

        // the deadline is saved a moment later, so work it out here for the email
        var due = ctx.Def.DueHours is > 0 ? DateTime.UtcNow.AddHours(ctx.Def.DueHours.Value) : (DateTime?)null;
        var mailError = await NotifyAsync(ctx.Instance, ctx.Step, list, ctx.Instance.StartedBy, ct, due);
        return StepResult.Wait(new { assignees = list, notifyError = mailError, away = awayNotes });
    }

    /// Best effort: a failed email never blocks the request. Returns the error text or null.
    public async Task<string?> NotifyAsync(ProcessInstance inst, StepInstance step, IEnumerable<string> to,
        string? by, CancellationToken ct, DateTime? due = null)
    {
        try
        {
            var emails = await _ids.ToEmailsAsync(to, ct);
            if (emails.Count == 0) return null;
            var tpl = new MailTemplate
            {
                Kind = "task",
                Title = inst.Title,
                Intro = "A task has been assigned to you.",
                Details = Rows(inst, step, due ?? step.DueAt, by),
                ButtonLabel = "Open the task",
                ButtonUrl = Link(inst)
            };
            await _mail.SendTemplateAsync(emails, $"Task assigned: {inst.Title}", tpl, ct);
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
            var rows = new List<MailDetail> { new("Process", inst.ProcessName) };
            if (!string.IsNullOrWhiteSpace(by)) rows.Add(new("Added by", by == "system:poll" ? "Started automatically" : by));
            var tpl = new MailTemplate
            {
                Kind = "observer",
                Title = inst.Title,
                Intro = "You have been added as an observer. You can follow this request, but you cannot act on it.",
                Details = rows,
                ButtonLabel = "Open the request",
                ButtonUrl = Link(inst)
            };
            await _mail.SendTemplateAsync(emails, $"You are observing: {inst.Title}", tpl, ct);
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
            var n = ordered.Count;

            var tpl = new MailTemplate
            {
                Kind = late > 0 ? "overdue" : "reminder",
                Label = "Your open tasks",
                Title = $"{n} task{(n == 1 ? "" : "s")} waiting for you",
                Intro = late > 0 ? $"{late} of them {(late == 1 ? "is" : "are")} overdue." : "Here is what is waiting for you.",
                Items = ordered.Select(i => new MailItem(
                    i.Inst.Title,
                    $"{i.Step.StepName} · {i.Inst.ProcessName}",
                    i.Step.DueAt == null ? null : "Due " + _time.Format(i.Step.DueAt),
                    Link(i.Inst),
                    i.Overdue ? "Overdue" : null)).ToList(),
                ButtonLabel = "Open My Tasks",
                ButtonUrl = Base + "/tasks"
            };
            await _mail.SendTemplateAsync(new[] { email },
                $"Reminder: {n} task{(n == 1 ? "" : "s")} waiting for you" + (late > 0 ? $" ({late} overdue)" : ""), tpl, ct);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning("Digest email to {Email} failed: {Msg}", email, ex.Message);
            return ex.Message;
        }
    }

    public async Task SendEscalationAsync(ProcessInstance inst, StepInstance step, string action,
        IReadOnlyList<string> targets, IReadOnlyList<string> originals, string? extra, CancellationToken ct)
    {
        try
        {
            var toTargets = await _ids.ToEmailsAsync(targets, ct);
            if (toTargets.Count > 0)
            {
                var tpl = new MailTemplate
                {
                    Kind = action == "forward" ? "escalated" : "deadline",
                    Title = inst.Title,
                    Intro = action == "forward"
                        ? "A task was not handled by its deadline and has been forwarded to you."
                        : "A task passed its deadline. You are being told because of the escalation rules.",
                    Message = string.IsNullOrWhiteSpace(extra) ? null : extra,
                    Details = Rows(inst, step, step.DueAt, null, "Was due"),
                    ButtonLabel = "Open the request",
                    ButtonUrl = Link(inst)
                };
                await _mail.SendTemplateAsync(toTargets, $"Escalated: {inst.Title}", tpl, ct);
            }

            var who = originals.ToList();
            if (!string.IsNullOrWhiteSpace(inst.StartedBy)) who.Add(inst.StartedBy!);
            var toOthers = (await _ids.ToEmailsAsync(who, ct)).Except(toTargets, StringComparer.OrdinalIgnoreCase).ToList();
            if (toOthers.Count > 0)
            {
                var tpl = new MailTemplate
                {
                    Kind = action == "reject" ? "rejected" : "deadline",
                    Title = inst.Title,
                    Intro = action switch
                    {
                        "forward" => "A task was not handled by its deadline and has been forwarded to someone else.",
                        "reject" => "A task was not handled by its deadline, so the request was rejected automatically.",
                        _ => "A task passed its deadline."
                    },
                    Details = Rows(inst, step, step.DueAt, null, "Was due"),
                    ButtonLabel = "Open the request",
                    ButtonUrl = Link(inst)
                };
                await _mail.SendTemplateAsync(toOthers, $"Deadline passed: {inst.Title}", tpl, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { _log.LogWarning("Escalation email failed for {Id}: {Msg}", inst.Id, ex.Message); }
    }
}