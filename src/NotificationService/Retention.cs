using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace NotificationService;

public static class LogRetention
{
    /// Deletes sent / dry-run emails older than `days` and failed ones older than `failedDays` (0 = keep).
    public static async Task<int> RunAsync(NotifyDb db, int days, int failedDays, CancellationToken ct)
    {
        var n = await PurgeAsync(db, x => x.Status != NotifyStatus.Failed, days, ct);
        n += await PurgeAsync(db, x => x.Status == NotifyStatus.Failed, failedDays, ct);
        return n;
    }

    private static async Task<int> PurgeAsync(NotifyDb db, Expression<Func<NotificationLog, bool>> kind, int days, CancellationToken ct)
    {
        if (days <= 0) return 0;
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var removed = 0;
        while (!ct.IsCancellationRequested)
        {
            var ids = await db.Notifications.Where(kind).Where(x => x.CreatedAt < cutoff)
                .OrderBy(x => x.CreatedAt).Select(x => x.Id).Take(500).ToListAsync(ct);
            if (ids.Count == 0) break;
            db.Notifications.RemoveRange(ids.Select(id => new NotificationLog { Id = id }));   // stubs: no need to load the bodies
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            removed += ids.Count;
        }
        return removed;
    }
}

public class RetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _cfg;
    private readonly ILogger<RetentionWorker> _log;

    public RetentionWorker(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<RetentionWorker> log)
    { _scopes = scopes; _cfg = cfg; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<NotifyDb>();
                    var n = await LogRetention.RunAsync(db, _cfg.GetValue("Retention:Days", 90), _cfg.GetValue("Retention:FailedDays", 180), ct);
                    if (n > 0) _log.LogInformation("Retention: deleted {Count} old email log row(s).", n);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { _log.LogWarning("Retention run failed: {Msg}", ex.Message); }
                await Task.Delay(TimeSpan.FromHours(24), ct);
            }
        }
        catch (OperationCanceledException) { }
    }
}