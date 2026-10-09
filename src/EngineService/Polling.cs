using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProcessChecker.Contracts;

namespace EngineService;

public record PollResult(string ProcessKey, int Found, int Created, int Skipped, List<string> Errors);

public class PollingService
{
    private readonly ConnectorClient _conn;
    private readonly WorkflowEngine _engine;
    private readonly EngineDb _db;
    private readonly IConfiguration _cfg;

    public PollingService(ConnectorClient conn, WorkflowEngine engine, EngineDb db, IConfiguration cfg)
    {
        _conn = conn; _engine = engine; _db = db; _cfg = cfg;
    }

    public async Task<PollResult> PollAsync(DefinitionInfo info, CancellationToken ct)
    {
        var t = info.Definition.Trigger;
        if (t.Type != TriggerTypes.DatabasePoll)
            throw new EngineException(400, "This process does not use a DatabasePoll trigger.");

        var errors = new List<string>();
        int created = 0, skipped = 0;

        QueryOutput res;
        try
        {
            res = await _conn.QueryAsync(t.Connection!, t.Query!, null,
                _cfg.GetValue("Polling:MaxRowsPerPoll", 200), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new PollResult(info.Key, 0, 0, 0, new List<string> { ex.Message });
        }

        foreach (var raw in res.Rows)
        {
            ct.ThrowIfCancellationRequested();
            var row = new Dictionary<string, JsonElement>(raw, StringComparer.OrdinalIgnoreCase);

            string key;
            try { key = KeyOf(row, t.KeyColumn); }
            catch (Exception ex) { errors.Add(ex.Message); continue; }

            if (await _db.TriggerSeenRows.AnyAsync(x => x.ProcessKey == info.Key && x.SourceKey == key, ct))
            {
                skipped++;
                continue;
            }

            try
            {
                await _engine.StartAsync(info, DataBag.FromDictionary(row),
                    "system:poll", $"{info.Name} #{key}", key, ct);
                created++;
            }
            catch (DbUpdateException) { skipped++; }          // another poll got there first
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"{key}: {ex.Message}");
            }
            finally { _db.ChangeTracker.Clear(); }
        }

        return new PollResult(info.Key, res.Rows.Count, created, skipped, errors);
    }

    private static string KeyOf(Dictionary<string, JsonElement> row, string? keyColumn)
    {
        if (!string.IsNullOrWhiteSpace(keyColumn))
        {
            if (!row.TryGetValue(keyColumn, out var v))
                throw new InvalidOperationException($"KeyColumn '{keyColumn}' is not in the query result.");
            var s = DataBag.Text(v).Trim();
            if (s.Length == 0) throw new InvalidOperationException($"KeyColumn '{keyColumn}' is empty for a row.");
            if (s.Length > 200) throw new InvalidOperationException($"KeyColumn value is longer than 200 characters.");
            return s;
        }

        // No KeyColumn: identify the row by a hash of all its values
        var canonical = string.Join("|", row
            .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
            .Select(k => $"{k.Key}={DataBag.Text(k.Value)}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..32];
    }
}

/// Wakes up every 30 seconds and polls the processes whose interval has passed.
public class PollingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _cfg;
    private readonly ILogger<PollingWorker> _log;
    private readonly Dictionary<string, DateTime> _lastRun = new();

    public PollingWorker(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<PollingWorker> log)
    {
        _scopes = scopes; _cfg = cfg; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_cfg.GetValue("Polling:Enabled", true))
        {
            _log.LogInformation("Polling is disabled.");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);   // let the other services start first
            while (!ct.IsCancellationRequested)
            {
                try { await TickAsync(ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogWarning("Polling tick failed: {Msg}", ex.Message);
                }
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        List<DefinitionInfo> list;
        using (var scope = _scopes.CreateScope())
            list = await scope.ServiceProvider.GetRequiredService<DefinitionClient>().ListAsync(ct);

        foreach (var info in list.Where(d => d.IsActive && d.Definition.Trigger.Type == TriggerTypes.DatabasePoll))
        {
            var every = TimeSpan.FromMinutes(Math.Max(1, info.Definition.Trigger.EveryMinutes));
            if (_lastRun.TryGetValue(info.Key, out var last) && DateTime.UtcNow - last < every) continue;
            _lastRun[info.Key] = DateTime.UtcNow;

            using var scope = _scopes.CreateScope();
            var r = await scope.ServiceProvider.GetRequiredService<PollingService>().PollAsync(info, ct);

            if (r.Errors.Count > 0)
                _log.LogWarning("Poll {Key}: {Errors}", info.Key, string.Join(" | ", r.Errors));
            else if (r.Created > 0)
                _log.LogInformation("Poll {Key}: started {Created} new request(s)", info.Key, r.Created);
        }
    }
}