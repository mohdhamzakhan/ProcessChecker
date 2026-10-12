using EngineService;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Oracle.ManagedDataAccess.Client;
using ProcessChecker.Contracts;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

builder.Services.AddDbContext<EngineDb>(o => o.UseOracle(cfg.GetConnectionString("Default")));

var gk = cfg["Gateway:Key"];
void AddKey(HttpClient c) { if (!string.IsNullOrWhiteSpace(gk)) c.DefaultRequestHeaders.Add(InternalAuth.KeyHeader, gk); }

builder.Services.AddHttpClient<DefinitionClient>(c =>
{ c.BaseAddress = new Uri(cfg["Services:Definition"]!); c.Timeout = TimeSpan.FromSeconds(30); AddKey(c); });
builder.Services.AddHttpClient<ConnectorClient>(c =>
{ c.BaseAddress = new Uri(cfg["Services:Connector"]!); c.Timeout = TimeSpan.FromSeconds(90); AddKey(c); });
builder.Services.AddHttpClient<NotificationClient>(c =>
{ c.BaseAddress = new Uri(cfg["Services:Notification"]!); c.Timeout = TimeSpan.FromSeconds(30); AddKey(c); });

// One handler per step type. A new step type = a new class + one line here.
builder.Services.AddScoped<IStepHandler, ApprovalHandler>();
builder.Services.AddScoped<IStepHandler, FormHandler>();
builder.Services.AddScoped<IStepHandler, UploadHandler>();
builder.Services.AddScoped<IStepHandler, DispatchHandler>();
builder.Services.AddScoped<IStepHandler, NotifyHandler>();
builder.Services.AddScoped<IStepHandler, DataFetchHandler>();
builder.Services.AddScoped<IStepHandler, LinkHandler>();
builder.Services.AddScoped<TaskMailer>();

builder.Services.AddScoped<WorkflowEngine>();
builder.Services.AddScoped<PollingService>();
builder.Services.AddHostedService<PollingWorker>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();
app.UseInternalKey();
app.UseSwagger();
app.UseSwaggerUI();

// Turn EngineException into a clean JSON error
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (EngineException ex)
    {
        ctx.Response.StatusCode = ex.Status;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message, errors = ex.Errors });
    }
});

// Create this service's tables (the schema already has other services' tables)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<EngineDb>();
    var creator = db.Database.GetService<IRelationalDatabaseCreator>();
    try { creator.CreateTables(); }
    catch (OracleException ex) when (ex.Number == 955) { /* tables already exist */ }
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

// ------------------------------------------------------------ start / list / detail
app.MapPost("/api/instances/start", async (HttpContext http, StartRequest req, WorkflowEngine engine, CancellationToken ct) =>
{
    if (http.Caller() is { } who) req = req with { StartedBy = who };
    var inst = await engine.StartAsync(req, ct);
    return Results.Created($"/api/instances/{inst.Id}", Mapper.ToDetail(inst));
});

async Task<IResult> Act(HttpContext http, WorkflowEngine engine, EngineDb db, Guid id, string stepKey,
    string action, ActRequest? r, CancellationToken ct)
{
    r ??= new ActRequest(null, null, null, null);
    if (http.Caller() is { } who) r = r with { By = who };     // nobody can act as someone else
    await engine.ActAsync(id, stepKey, new StepAction(action, r.By, r.Note, r.Values, r.Files, http.IsAdmin()), ct);

    var inst = await db.Instances.AsNoTracking()
        .Include(i => i.Steps).ThenInclude(s => s.Approvals)
        .FirstAsync(i => i.Id == id, ct);
    return Results.Ok(Mapper.ToDetail(inst));
}

app.MapGet("/api/instances", async (
    EngineDb db, string? status, string? processKey, string? q, string ? startedBy, int ? page, int? pageSize, CancellationToken ct) =>
{
    IQueryable<ProcessInstance> query = db.Instances.AsNoTracking().Include(i => i.Steps);
    if (!string.IsNullOrWhiteSpace(status)) query = query.Where(i => i.Status == status);
    if (!string.IsNullOrWhiteSpace(processKey)) query = query.Where(i => i.ProcessKey == processKey);
    if (!string.IsNullOrWhiteSpace(q)) query = query.Where(i => i.Title.Contains(q));
    if (!string.IsNullOrWhiteSpace(startedBy)) query = query.Where(i => i.StartedBy == startedBy);

    var size = Math.Clamp(pageSize ?? 25, 1, 100);
    var p = Math.Max(page ?? 1, 1);
    var total = await query.CountAsync(ct);
    var items = await query.OrderByDescending(i => i.CreatedAt).Skip((p - 1) * size).Take(size).ToListAsync(ct);

    return Results.Ok(new { total, page = p, pageSize = size, items = items.Select(Mapper.ToSummary) });
});

app.MapGet("/api/instances/{id:guid}", async (Guid id, EngineDb db, CancellationToken ct) =>
{
    var inst = await db.Instances.AsNoTracking()
        .Include(i => i.Steps).ThenInclude(s => s.Approvals)
        .FirstOrDefaultAsync(i => i.Id == id, ct);
    return inst is null ? Results.NotFound() : Results.Ok(Mapper.ToDetail(inst));
});

app.MapPost("/api/instances/{id:guid}/steps/{stepKey}/retry",
    async (Guid id, string stepKey, WorkflowEngine engine, EngineDb db, CancellationToken ct) =>
    {
        await engine.RetryAsync(id, stepKey, ct);
        var inst = await db.Instances.AsNoTracking()
            .Include(i => i.Steps).ThenInclude(s => s.Approvals)
            .FirstAsync(i => i.Id == id, ct);
        return Results.Ok(Mapper.ToDetail(inst));
    });

app.MapPost("/api/instances/{id:guid}/steps/{stepKey}/assign", async (
    HttpContext http, Guid id, string stepKey, ReassignRequest r,
    WorkflowEngine engine, EngineDb db, CancellationToken ct) =>
{
    if (!http.IsAdmin())
        return Results.Json(new { error = "Only an admin can assign tasks." }, statusCode: 403);

    await engine.ReassignAsync(id, stepKey, r.Assignees ?? Array.Empty<string>(), http.Caller(), ct);

    var inst = await db.Instances.AsNoTracking()
        .Include(i => i.Steps).ThenInclude(s => s.Approvals)
        .FirstAsync(i => i.Id == id, ct);
    return Results.Ok(Mapper.ToDetail(inst));
});
// ------------------------------------------------------------ act on a step

app.MapPost("/api/instances/{id:guid}/steps/{stepKey}/approve",
    (HttpContext http, Guid id, string stepKey, ActRequest? r, WorkflowEngine e, EngineDb db, CancellationToken ct) =>
        Act(http, e, db, id, stepKey, "approve", r, ct));

app.MapPost("/api/instances/{id:guid}/steps/{stepKey}/reject",
    (HttpContext http, Guid id, string stepKey, ActRequest? r, WorkflowEngine e, EngineDb db, CancellationToken ct) =>
        Act(http, e, db, id, stepKey, "reject", r, ct));

app.MapPost("/api/instances/{id:guid}/steps/{stepKey}/submit",
    (HttpContext http, Guid id, string stepKey, ActRequest? r, WorkflowEngine e, EngineDb db, CancellationToken ct) =>
        Act(http, e, db, id, stepKey, "submit", r, ct));

app.MapPost("/api/instances/{id:guid}/steps/{stepKey}/upload",
    (HttpContext http, Guid id, string stepKey, ActRequest? r, WorkflowEngine e, EngineDb db, CancellationToken ct) =>
        Act(http, e, db, id, stepKey, "upload", r, ct));

app.MapPost("/api/instances/{id:guid}/cancel", async (
    HttpContext http, Guid id, EngineDb db, WorkflowEngine engine, CancellationToken ct) =>
{
    if (http.Caller() is { } who && !http.IsAdmin())
    {
        var starter = await db.Instances.AsNoTracking().Where(i => i.Id == id)
            .Select(i => i.StartedBy).FirstOrDefaultAsync(ct);
        if (!string.Equals(starter, who, StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { error = "Only the person who started this request, or an admin, can cancel it." },
                statusCode: 403);
    }
    await engine.CancelAsync(id, ct);
    return Results.NoContent();
});

// ------------------------------------------------------------ inbox + dashboard
app.MapGet("/api/tasks", async (HttpContext http, EngineDb db, string? user, CancellationToken ct) =>
{
    var email = (http.Caller() ?? user)?.Trim().ToLowerInvariant();

    var approvals = string.IsNullOrEmpty(email)
        ? new List<TaskItem>()
        : await db.Approvals.AsNoTracking()
            .Where(a => a.Decision == null && a.Approver == email
                        && a.Step.Status == StepState.InProgress
                        && a.Step.Instance.Status == InstanceStatus.Running)
            .Select(a => new TaskItem(a.Step.InstanceId, a.Step.Instance.Title, a.Step.Instance.ProcessName,
                a.Step.StepKey, a.Step.StepName, a.Step.StepType, a.Step.StartedAt, null))
            .ToListAsync(ct);

    var waiting = await db.Steps.AsNoTracking()
        .Where(s => s.Status == StepState.InProgress
                    && (s.StepType == StepTypes.Form || s.StepType == StepTypes.Upload)
                    && s.Instance.Status == InstanceStatus.Running)
        .Select(s => new TaskItem(s.InstanceId, s.Instance.Title, s.Instance.ProcessName,
            s.StepKey, s.StepName, s.StepType, s.StartedAt, s.Assignees))
        .ToListAsync(ct);

    bool Mine(TaskItem t) => email != null && TaskMailer.Split(t.Assignees).Contains(email);

    var mine = approvals.Concat(waiting.Where(Mine)).OrderBy(t => t.Since).ToList();
    var open = waiting.Where(t => string.IsNullOrEmpty(t.Assignees)).OrderBy(t => t.Since).ToList();
    var others = http.IsAdmin()
        ? waiting.Where(t => !string.IsNullOrEmpty(t.Assignees) && !Mine(t))
            .Select(t => t with { Assignees = string.Join(", ", TaskMailer.Split(t.Assignees)) })
            .OrderBy(t => t.Since).ToList()
        : new List<TaskItem>();

    return Results.Ok(new { mine, open, others });
});

app.MapGet("/api/dashboard", async (EngineDb db, CancellationToken ct) =>
{
    var rows = await db.Instances.AsNoTracking()
        .GroupBy(i => new { i.ProcessKey, i.ProcessName, i.Status })
        .Select(g => new { g.Key.ProcessKey, g.Key.ProcessName, g.Key.Status, Count = g.Count() })
        .ToListAsync(ct);

    var byStatus = rows.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Sum(x => x.Count));
    var byProcess = rows.GroupBy(r => new { r.ProcessKey, r.ProcessName })
        .Select(g => new
        {
            g.Key.ProcessKey,
            g.Key.ProcessName,
            total = g.Sum(x => x.Count),
            byStatus = g.ToDictionary(x => x.Status, x => x.Count)
        }).ToList();

    var today = DateTime.UtcNow.Date;
    var since = today.AddDays(-13);
    var created = await db.Instances.AsNoTracking()
        .Where(i => i.CreatedAt >= since).Select(i => i.CreatedAt).ToListAsync(ct);
    var perDay = Enumerable.Range(0, 14).Select(n => since.AddDays(n))
        .Select(d => new { date = d.ToString("yyyy-MM-dd"), count = created.Count(c => c.Date == d) }).ToList();

    var from30 = today.AddDays(-30);
    var done = await db.Instances.AsNoTracking()
        .Where(i => i.Status == InstanceStatus.Completed && i.CompletedAt != null && i.CompletedAt >= from30)
        .Select(i => new { i.CreatedAt, i.CompletedAt }).ToListAsync(ct);
    double? avgHours = done.Count == 0 ? null
        : Math.Round(done.Average(x => (x.CompletedAt!.Value - x.CreatedAt).TotalHours), 1);

    return Results.Ok(new
    {
        total = rows.Sum(r => r.Count),
        byStatus,
        byProcess,
        perDay,
        avgHours,
        completed30 = done.Count
    });
});

// ------------------------------------------------------------ poll now (for testing)
app.MapPost("/api/poll/{key}/run-now", async (
    string key, DefinitionClient defs, PollingService poller, CancellationToken ct) =>
{
    var info = await defs.GetLatestAsync(key, ct)
               ?? throw new EngineException(404, $"Process '{key}' was not found.");
    return Results.Ok(await poller.PollAsync(info, ct));
});

app.Run();