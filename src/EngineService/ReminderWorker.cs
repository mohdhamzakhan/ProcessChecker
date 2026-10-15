using Microsoft.EntityFrameworkCore;
using ProcessChecker.Contracts;

namespace EngineService;

/// Every few minutes: emails the people a waiting step is for, once its reminder interval has passed.
public class ReminderWorker : BackgroundService
{
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
        var max = _cfg.GetValue("Reminders:MaxPerStep", 10);
        var now = DateTime.UtcNow;

        var steps = await db.Steps.Include(s => s.Instance).Include(s => s.Approvals)
            .Where(s => s.Status == StepState.InProgress && s.RemindHours != null && s.RemindHours > 0
                        && s.ReminderCount < max && s.Instance.Status == InstanceStatus.Running)
            .ToListAsync(ct);

        foreach (var s in steps)
        {
            var last = s.LastReminderAt ?? s.StartedAt;
            if (last == null || last.Value.AddHours(s.RemindHours!.Value) > now) continue;

            List<string> who;
            if (s.StepType == StepTypes.Approval)
                who = s.Approvals.Where(a => a.Decision == null).Select(a => a.Approver).ToList();
            else if (TaskMailer.Split(s.Assignees) is { Count: > 0 } assigned)
                who = assigned;
            else
                who = s.Instance.StartedBy is { } sb && sb.Contains('@') ? new List<string> { sb } : new List<string>();

            if (who.Count > 0)
            {
                var error = await mailer.SendReminderAsync(s.Instance, s, who, s.DueAt != null && s.DueAt < now, ct);
                if (error != null) continue;                    // try again at the next run
            }
            s.LastReminderAt = now;
            s.ReminderCount++;
            await db.SaveChangesAsync(ct);
        }
    }
}