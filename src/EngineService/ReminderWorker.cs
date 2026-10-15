using Microsoft.EntityFrameworkCore;
using ProcessChecker.Contracts;

namespace EngineService;

/// Every few minutes: when a waiting step's reminder is due, the person gets ONE email listing
/// all of their waiting tasks that have reminders (overdue first).
public class ReminderWorker : BackgroundService
{
    private record Candidate(StepInstance Step, List<string> Emails, bool Due);

    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _cfg;
    private readonly ILogger<ReminderWorker> _log;

    public ReminderWorker(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<ReminderWorker> log)
    { _scopes = scopes; _cfg = cfg; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_cfg.GetValue("Reminders:Enabled", true)) { _log.LogInformation("Reminders are disabled."); return; }
        var every = TimeSpan.FromMinutes(Math.Max(1, _cfg.GetValue("Reminders:CheckMinutes", 5)));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            while (!ct.IsCancellationRequested)
            {
                try { await RunOnceAsync(ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { _log.LogWarning("Reminder run failed: {Msg}", ex.Message); }
                await Task.Delay(every, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EngineDb>();
        var mailer = scope.ServiceProvider.GetRequiredService<TaskMailer>();
        var resolver = scope.ServiceProvider.GetRequiredService<IdentityResolver>();
        var max = _cfg.GetValue("Reminders:MaxPerStep", 10);
        var now = DateTime.UtcNow;

        var steps = await db.Steps.Include(s => s.Instance).Include(s => s.Approvals)
            .Where(s => s.Status == StepState.InProgress && s.RemindHours != null && s.RemindHours > 0
                        && s.ReminderCount < max && s.Instance.Status == InstanceStatus.Running)
            .ToListAsync(ct);
        if (steps.Count == 0) return;

        // who each waiting step is for, as email addresses (looked up once per distinct group of people)
        var cache = new Dictionary<string, List<string>>();
        var cands = new List<Candidate>();
        foreach (var s in steps)
        {
            List<string> who;
            if (s.StepType == StepTypes.Approval)
                who = s.Approvals.Where(a => a.Decision == null).Select(a => a.Approver).ToList();
            else if (TaskMailer.Split(s.Assignees) is { Count: > 0 } assigned)
                who = assigned;
            else
                who = s.Instance.StartedBy is { } sb && sb.Contains('@') ? new List<string> { sb } : new List<string>();

            var key = string.Join("|", who.OrderBy(x => x));
            if (!cache.TryGetValue(key, out var emails))
                cache[key] = emails = who.Count == 0 ? new List<string>() : await resolver.ToEmailsAsync(who, ct);

            var last = s.LastReminderAt ?? s.StartedAt;
            var due = last != null && last.Value.AddHours(s.RemindHours!.Value) <= now;
            cands.Add(new Candidate(s, emails, due));
        }

        // one digest for every person who has at least one due reminder
        var triggered = cands.Where(c => c.Due).SelectMany(c => c.Emails)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var sent = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var email in triggered)
        {
            var items = cands.Where(c => c.Emails.Contains(email, StringComparer.OrdinalIgnoreCase))
                .Select(c => new DigestItem(c.Step.Instance, c.Step, c.Step.DueAt != null && c.Step.DueAt < now))
                .ToList();
            sent[email] = await mailer.SendDigestAsync(email, items, ct) == null;
        }

        // a step counts as reminded when everybody it is for got their digest (a failure is retried next run)
        foreach (var c in cands)
        {
            var reached = c.Emails.Count == 0
                ? c.Due                                          // nobody to tell: do not retry for ever
                : c.Emails.All(e => sent.TryGetValue(e, out var ok) && ok);
            if (!reached) continue;
            c.Step.LastReminderAt = now;
            c.Step.ReminderCount++;
        }
        await db.SaveChangesAsync(ct);
    }
}