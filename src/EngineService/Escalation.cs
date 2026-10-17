using Microsoft.EntityFrameworkCore;

namespace EngineService;

public static class EscalationRunner
{
    public static async Task<int> RunOnceAsync(IServiceScopeFactory scopes, ILogger log, CancellationToken ct)
    {
        List<(Guid Id, string Key)> due;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EngineDb>();
            var now = DateTime.UtcNow;
            due = (await db.Steps.AsNoTracking()
                    .Where(s => s.Status == StepState.InProgress && s.EscalateAt != null && s.EscalatedAt == null
                                && s.EscalateAt <= now && s.Instance.Status == InstanceStatus.Running)
                    .OrderBy(s => s.EscalateAt).Select(s => new { s.InstanceId, s.StepKey }).Take(100).ToListAsync(ct))
                .Select(x => (x.InstanceId, x.StepKey)).ToList();
        }

        var done = 0;
        foreach (var (id, key) in due)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var scope = scopes.CreateScope();
                if (await scope.ServiceProvider.GetRequiredService<WorkflowEngine>().EscalateAsync(id, key, ct)) done++;
            }
            catch (EngineException ex) { log.LogInformation("Escalation of {Step} in {Id} skipped: {Msg}", key, id, ex.Message); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { log.LogWarning("Escalation of {Step} in {Id} failed: {Msg}", key, id, ex.Message); }
        }
        return done;
    }
}

public class EscalationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _cfg;
    private readonly ILogger<EscalationWorker> _log;

    public EscalationWorker(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<EscalationWorker> log)
    { _scopes = scopes; _cfg = cfg; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_cfg.GetValue("Escalation:Enabled", true)) { _log.LogInformation("Escalation is disabled."); return; }
        var every = TimeSpan.FromMinutes(Math.Max(1, _cfg.GetValue("Escalation:CheckMinutes", 2)));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), ct);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var n = await EscalationRunner.RunOnceAsync(_scopes, _log, ct);
                    if (n > 0) _log.LogInformation("Escalation: {Count} step(s) escalated.", n);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { _log.LogWarning("Escalation run failed: {Msg}", ex.Message); }
                await Task.Delay(every, ct);
            }
        }
        catch (OperationCanceledException) { }
    }
}