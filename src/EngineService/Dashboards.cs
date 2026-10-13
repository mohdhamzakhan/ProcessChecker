using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProcessChecker.Contracts;

namespace EngineService;

public class DashboardEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerEmail { get; set; } = "";
    public string Name { get; set; } = "";
    public string ConfigJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public record DashWidget(string? Id, string Type, string? Metric, string? Title);
public record DashConfig(List<string>? ProcessKeys, int RangeDays, List<DashWidget>? Widgets);
public record DashSave(string? Name, DashConfig? Config);

public static class DashboardEndpoints
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    private static readonly string[] Types = { "kpi", "status", "perDay", "perMonth", "byStep", "recent" };
    private static readonly string[] Metrics =
        { "total", "completed", "running", "rejected", "failed", "cancelled", "completionRate", "avgHours" };
    private static readonly int[] Ranges = { 0, 7, 30, 90, 365 };

    private static List<string> Validate(DashSave? r)
    {
        var errors = new List<string>();
        var name = r?.Name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > 200) errors.Add("Give the dashboard a name (up to 200 characters).");

        var c = r?.Config;
        if (c is null) { errors.Add("The dashboard layout is missing."); return errors; }
        if ((c.ProcessKeys?.Count ?? 0) > 50) errors.Add("Too many processes selected.");
        if (!Ranges.Contains(c.RangeDays)) errors.Add("Choose a valid time range.");

        var widgets = c.Widgets ?? new List<DashWidget>();
        if (widgets.Count == 0) errors.Add("Add at least one widget.");
        if (widgets.Count > 20) errors.Add("A dashboard can have at most 20 widgets.");
        foreach (var w in widgets)
        {
            if (!Types.Contains(w.Type)) errors.Add($"Unknown widget '{w.Type}'.");
            else if (w.Type == "kpi" && !Metrics.Contains(w.Metric ?? "")) errors.Add("A number widget needs a valid metric.");
            if ((w.Title?.Length ?? 0) > 100) errors.Add("A widget title is too long (100 characters).");
        }
        return errors;
    }

    private static DashConfig Clean(DashConfig c) => new(
        (c.ProcessKeys ?? new()).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct().ToList(),
        c.RangeDays,
        (c.Widgets ?? new()).Select(w => w with
        {
            Id = string.IsNullOrWhiteSpace(w.Id) ? Guid.NewGuid().ToString("N")[..8] : w.Id,
            Title = string.IsNullOrWhiteSpace(w.Title) ? null : w.Title.Trim()
        }).ToList());

    private static object ToDto(DashboardEntity d) => new
    {
        id = d.Id,
        name = d.Name,
        updatedAt = d.UpdatedAt,
        config = JsonSerializer.Deserialize<DashConfig>(d.ConfigJson, J)
    };

    public static void MapDashboards(this WebApplication app)
    {
        var g = app.MapGroup("/api/dashboards");
        g.AddEndpointFilter(async (ctx, next) =>
            ctx.HttpContext.Caller() is null
                ? Results.Json(new { error = "Sign in to use dashboards." }, statusCode: 401)
                : await next(ctx));

        g.MapGet("", async (HttpContext http, EngineDb db, CancellationToken ct) =>
        {
            var me = http.Caller()!;
            var rows = await db.Dashboards.AsNoTracking().Where(d => d.OwnerEmail == me).OrderBy(d => d.Name).ToListAsync(ct);
            return Results.Ok(rows.Select(ToDto));
        });

        // the processes this person can build a dashboard on
        g.MapGet("scope", async (HttpContext http, EngineDb db, CancellationToken ct) =>
        {
            var rows = await db.Visible(http).AsNoTracking()
                .GroupBy(i => new { i.ProcessKey, i.ProcessName })
                .Select(x => new { key = x.Key.ProcessKey, name = x.Key.ProcessName, count = x.Count() })
                .ToListAsync(ct);
            return Results.Ok(rows.OrderBy(r => r.name));
        });

        g.MapPost("", async (HttpContext http, DashSave r, EngineDb db, CancellationToken ct) =>
        {
            var me = http.Caller()!;
            var errors = Validate(r);
            if (errors.Count > 0) return Results.BadRequest(new { error = "Validation failed.", errors });
            if (await db.Dashboards.CountAsync(d => d.OwnerEmail == me, ct) >= 20)
                return Results.BadRequest(new { error = "You can have at most 20 dashboards." });

            var e = new DashboardEntity
            {
                OwnerEmail = me,
                Name = r.Name!.Trim(),
                ConfigJson = JsonSerializer.Serialize(Clean(r.Config!), J)
            };
            db.Dashboards.Add(e);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/dashboards/{e.Id}", ToDto(e));
        });

        g.MapPut("{id:guid}", async (HttpContext http, Guid id, DashSave r, EngineDb db, CancellationToken ct) =>
        {
            var me = http.Caller()!;
            var e = await db.Dashboards.FirstOrDefaultAsync(d => d.Id == id && d.OwnerEmail == me, ct);
            if (e is null) return Results.NotFound();
            var errors = Validate(r);
            if (errors.Count > 0) return Results.BadRequest(new { error = "Validation failed.", errors });

            e.Name = r.Name!.Trim();
            e.ConfigJson = JsonSerializer.Serialize(Clean(r.Config!), J);
            e.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(e));
        });

        g.MapDelete("{id:guid}", async (HttpContext http, Guid id, EngineDb db, CancellationToken ct) =>
        {
            var me = http.Caller()!;
            var e = await db.Dashboards.FirstOrDefaultAsync(d => d.Id == id && d.OwnerEmail == me, ct);
            if (e is null) return Results.NotFound();
            db.Dashboards.Remove(e);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // the numbers: only for requests the caller is allowed to see
        g.MapPost("data", async (HttpContext http, DashConfig cfg, EngineDb db, CancellationToken ct) =>
        {
            var keys = (cfg.ProcessKeys ?? new()).Where(k => !string.IsNullOrWhiteSpace(k)).Take(50).ToList();
            IQueryable<ProcessInstance> scope = db.Visible(http).AsNoTracking();
            if (keys.Count > 0) scope = scope.Where(i => keys.Contains(i.ProcessKey));
            if (cfg.RangeDays > 0)
            {
                var from = DateTime.UtcNow.AddDays(-cfg.RangeDays);
                scope = scope.Where(i => i.CreatedAt >= from);
            }

            var rows = await scope.OrderByDescending(i => i.CreatedAt).Take(50000)
                .Select(i => new { i.Status, i.CreatedAt, i.CompletedAt }).ToListAsync(ct);

            var byStatus = rows.GroupBy(r => r.Status).ToDictionary(x => x.Key, x => x.Count());
            var done = rows.Where(r => r.Status == InstanceStatus.Completed && r.CompletedAt != null).ToList();
            double? avgHours = done.Count == 0 ? null
                : Math.Round(done.Average(r => (r.CompletedAt!.Value - r.CreatedAt).TotalHours), 1);

            var today = DateTime.UtcNow.Date;
            var days = cfg.RangeDays is 7 or 30 or 90 ? cfg.RangeDays : 30;
            var sDay = rows.GroupBy(r => r.CreatedAt.Date).ToDictionary(x => x.Key, x => x.Count());
            var cDay = done.GroupBy(r => r.CompletedAt!.Value.Date).ToDictionary(x => x.Key, x => x.Count());
            var perDay = Enumerable.Range(0, days).Select(n => today.AddDays(n - days + 1))
                .Select(d => new
                {
                    date = d.ToString("yyyy-MM-dd"),
                    started = sDay.GetValueOrDefault(d),
                    completed = cDay.GetValueOrDefault(d)
                }).ToList();

            var firstOfThis = new DateTime(today.Year, today.Month, 1);
            var sMon = rows.GroupBy(r => r.CreatedAt.ToString("yyyy-MM")).ToDictionary(x => x.Key, x => x.Count());
            var cMon = done.GroupBy(r => r.CompletedAt!.Value.ToString("yyyy-MM")).ToDictionary(x => x.Key, x => x.Count());
            var perMonth = Enumerable.Range(0, 12).Select(n => firstOfThis.AddMonths(n - 11).ToString("yyyy-MM"))
                .Select(m => new { month = m, started = sMon.GetValueOrDefault(m), completed = cMon.GetValueOrDefault(m) })
                .ToList();

            var byStep = await db.Steps.AsNoTracking()
                .Where(s => (s.Status == StepState.InProgress || s.Status == StepState.Failed)
                            && scope.Any(i => i.Id == s.InstanceId
                                              && (i.Status == InstanceStatus.Running || i.Status == InstanceStatus.Failed)))
                .GroupBy(s => s.StepName)
                .Select(x => new { step = x.Key, count = x.Count() })
                .ToListAsync(ct);

            var recent = await scope.Include(i => i.Steps).OrderByDescending(i => i.CreatedAt).Take(8).ToListAsync(ct);

            return Results.Ok(new
            {
                total = rows.Count,
                byStatus,
                avgHours,
                perDay,
                perMonth,
                byStep = byStep.OrderByDescending(x => x.count),
                recent = recent.Select(Mapper.ToSummary)
            });
        });
    }
}