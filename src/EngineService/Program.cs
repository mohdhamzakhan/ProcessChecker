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
builder.Services.AddHttpClient("docs", c => c.Timeout = TimeSpan.FromSeconds(60))
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var h = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseDefaultCredentials = cfg.GetValue("Documents:UseWindowsLogin", false)
        };
        if (cfg.GetValue("Documents:IgnoreCertificateErrors", false))
            h.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        return h;
    });

// One handler per step type. A new step type = a new class + one line here.
builder.Services.AddScoped<IStepHandler, ApprovalHandler>();
builder.Services.AddScoped<IStepHandler, FormHandler>();
builder.Services.AddScoped<IStepHandler, UploadHandler>();
builder.Services.AddScoped<IStepHandler, DispatchHandler>();
builder.Services.AddScoped<IStepHandler, NotifyHandler>();
builder.Services.AddScoped<IStepHandler, DataFetchHandler>();
builder.Services.AddScoped<IStepHandler, LinkHandler>();
builder.Services.AddScoped<IStepHandler, DecisionHandler>();
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
app.MapDashboards();

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
    await db.Database.OpenConnectionAsync();
    try
    {
        var conn = db.Database.GetDbConnection();
        if (!await DbObjects.TableExistsAsync(conn, "PC_INSTANCES"))
            db.Database.GetService<IRelationalDatabaseCreator>().CreateTables();    // first install: everything
        else
            await EnsureObjectsAsync(db, conn);                                    // upgrade: only what is missing
    }
    finally { await db.Database.CloseConnectionAsync(); }

    // one time: give requests that already exist their participants
    if (!await db.Participants.AnyAsync() && await db.Instances.AnyAsync())
    {
        var all = await db.Instances.Include(i => i.Steps).ThenInclude(s => s.Approvals).ToListAsync();
        foreach (var i in all)
            foreach (var (email, role) in ParticipantRules.Wanted(i).Distinct())
                db.Participants.Add(new Participant { InstanceId = i.Id, Email = email, Role = role });
        await db.SaveChangesAsync();
    }
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
    if (!await db.CanWorkAsync(http, id, ct))
        return Results.Json(new { error = "You do not have access to this request." }, statusCode: 403);

    r ??= new ActRequest(null, null, null, null);
    if (http.Caller() is { } who) r = r with { By = who };     // nobody can act as someone else
    await engine.ActAsync(id, stepKey, new StepAction(action, r.By, r.Note, r.Values, r.Files, http.IsAdmin()), ct);

    var inst = await db.Instances.AsNoTracking()
        .Include(i => i.Steps).ThenInclude(s => s.Approvals)

        .FirstAsync(i => i.Id == id, ct);
    return Results.Ok(Mapper.ToDetail(inst));
}

app.MapGet("/api/instances", async (
    HttpContext http, EngineDb db, string? status, string? processKey, string? q,
    string? startedBy, string? role, int? page, int? pageSize, CancellationToken ct) =>
{
    IQueryable<ProcessInstance> query = db.Visible(http).AsNoTracking();
    if (!string.IsNullOrWhiteSpace(status)) query = query.Where(i => i.Status == status);
    if (!string.IsNullOrWhiteSpace(processKey)) query = query.Where(i => i.ProcessKey == processKey);
    if (!string.IsNullOrWhiteSpace(q)) query = query.Where(i => i.Title.Contains(q));
    if (!string.IsNullOrWhiteSpace(startedBy)) query = query.Where(i => i.StartedBy == startedBy);

    var me = http.Caller();
    string[]? roles = role switch
    {
        "started" => new[] { PartRole.Starter },
        "assigned" => new[] { PartRole.Assignee, PartRole.Approver },
        "observer" => new[] { PartRole.Observer },
        "requested" => new[] { PartRole.Requester },
        _ => null
    };
    if (roles != null)
    {
        var ids = http.Identities();
        query = query.Where(i => db.Participants.Any(p => p.InstanceId == i.Id && ids.Contains(p.Email) && roles.Contains(p.Role)));
    }

    var size = Math.Clamp(pageSize ?? 25, 1, 100);
    var pageNo = Math.Max(page ?? 1, 1);
    var total = await query.CountAsync(ct);
    var items = await query.Include(i => i.Steps)
        .OrderByDescending(i => i.CreatedAt).Skip((pageNo - 1) * size).Take(size).ToListAsync(ct);

    return Results.Ok(new { total, page = pageNo, pageSize = size, items = items.Select(Mapper.ToSummary) });
});

app.MapGet("/api/instances/{id:guid}", async (HttpContext http, Guid id, EngineDb db, CancellationToken ct) =>
{
    var inst = await db.Visible(http).AsNoTracking()
        .Include(i => i.Steps).ThenInclude(s => s.Approvals)
        .FirstOrDefaultAsync(i => i.Id == id, ct);
    return inst is null ? Results.NotFound() : Results.Ok(Mapper.ToDetail(inst));
});

app.MapPost("/api/instances/{id:guid}/steps/{stepKey}/retry", async (
    HttpContext http, Guid id, string stepKey, WorkflowEngine engine, EngineDb db, CancellationToken ct) =>
{
    if (!await db.CanWorkAsync(http, id, ct))
        return Results.Json(new { error = "You do not have access to this request." }, statusCode: 403);

    await engine.RetryAsync(id, stepKey, ct);
    var inst = await db.Instances.AsNoTracking()
        .Include(i => i.Steps).ThenInclude(s => s.Approvals).FirstAsync(i => i.Id == id, ct);
    return Results.Ok(Mapper.ToDetail(inst));
});

app.MapPost("/api/instances/{id:guid}/observers", async (
    HttpContext http, Guid id, ObserversRequest r, WorkflowEngine engine, EngineDb db, CancellationToken ct) =>
{
    var info = await db.Instances.AsNoTracking().Where(i => i.Id == id)
        .Select(i => new { i.StartedBy }).FirstOrDefaultAsync(ct);
    if (info is null) return Results.NotFound();

    var who = http.Caller();
    if (!http.IsAdmin() && !string.Equals(info.StartedBy, who, StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "Only an admin or the person who started the request can change the observers." },
            statusCode: 403);

    await engine.SetObserversAsync(id, r.Observers ?? Array.Empty<string>(), who, ct);
    var inst = await db.Instances.AsNoTracking()
        .Include(i => i.Steps).ThenInclude(s => s.Approvals).FirstAsync(i => i.Id == id, ct);
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
    var isAdmin = http.IsAdmin();
    var tag = email == null ? null : ";" + email + ";";

    var approvals = email == null
        ? new List<TaskItem>()
        : await db.Approvals.AsNoTracking()
            .Where(a => a.Decision == null && a.Approver == email
                        && a.Step.Status == StepState.InProgress
                        && a.Step.Instance.Status == InstanceStatus.Running)
            .Select(a => new TaskItem(a.Step.InstanceId, a.Step.Instance.Title, a.Step.Instance.ProcessName,
                a.Step.StepKey, a.Step.StepName, a.Step.StepType, a.Step.StartedAt, null))
            .ToListAsync(ct);

    var waiting = db.Steps.AsNoTracking()
        .Where(s => s.Status == StepState.InProgress
                    && (s.StepType == StepTypes.Form || s.StepType == StepTypes.Upload)
                    && s.Instance.Status == InstanceStatus.Running);

    var assigned = tag == null
        ? new List<TaskItem>()
        : await waiting.Where(s => s.Assignees != null && s.Assignees.Contains(tag))
            .Select(s => new TaskItem(s.InstanceId, s.Instance.Title, s.Instance.ProcessName,
                s.StepKey, s.StepName, s.StepType, s.StartedAt, s.Assignees))
            .ToListAsync(ct);

    // no one assigned: only the person who started the request (or an admin) can do it
    var open = await waiting
        .Where(s => s.Assignees == null
                    && (isAdmin || db.Participants.Any(p => p.InstanceId == s.InstanceId
                                                            && p.Email == email && p.Role == PartRole.Starter)))
        .Select(s => new TaskItem(s.InstanceId, s.Instance.Title, s.Instance.ProcessName,
            s.StepKey, s.StepName, s.StepType, s.StartedAt, null))
        .ToListAsync(ct);

    var others = isAdmin
        ? (await waiting.Where(s => s.Assignees != null && (tag == null || !s.Assignees.Contains(tag)))
            .Select(s => new TaskItem(s.InstanceId, s.Instance.Title, s.Instance.ProcessName,
                s.StepKey, s.StepName, s.StepType, s.StartedAt, s.Assignees))
            .ToListAsync(ct))
          .Select(t => t with { Assignees = string.Join(", ", TaskMailer.Split(t.Assignees)) }).ToList()
        : new List<TaskItem>();

    var ids = http.Identities();

    var observing = ids.Count == 0
        ? new List<InstanceSummary>()
        : (await db.Instances.AsNoTracking().Include(i => i.Steps)
            .Where(i => i.Status == InstanceStatus.Running
                        && db.Participants.Any(p => p.InstanceId == i.Id && ids.Contains(p.Email) && p.Role == PartRole.Observer))
            .OrderByDescending(i => i.CreatedAt).Take(50).ToListAsync(ct))
          .Select(Mapper.ToSummary).ToList();

    var requested = ids.Count == 0
        ? new List<InstanceSummary>()
        : (await db.Instances.AsNoTracking().Include(i => i.Steps)
            .Where(i => db.Participants.Any(p => p.InstanceId == i.Id && ids.Contains(p.Email) && p.Role == PartRole.Requester))
            .OrderByDescending(i => i.CreatedAt).Take(50).ToListAsync(ct))
          .Select(Mapper.ToSummary).ToList();

    return Results.Ok(new
    {
        mine = approvals.Concat(assigned).OrderBy(t => t.Since).ToList(),
        requested,
        observing,
        open = open.OrderBy(t => t.Since).ToList(),
        others = others.OrderBy(t => t.Since).ToList()
    });
});

app.MapGet("/api/dashboard", async (HttpContext http, EngineDb db, CancellationToken ct) =>
{
    var scope = db.Visible(http).AsNoTracking();

    var rows = await scope
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
    var created = await scope.Where(i => i.CreatedAt >= since).Select(i => i.CreatedAt).ToListAsync(ct);
    var perDay = Enumerable.Range(0, 14).Select(n => since.AddDays(n))
        .Select(d => new { date = d.ToString("yyyy-MM-dd"), count = created.Count(c => c.Date == d) }).ToList();

    var from30 = today.AddDays(-30);
    var done = await scope
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

app.MapPost("/api/instances/resolve-requesters", async (
    HttpContext http, string processKey, DefinitionClient defs, WorkflowEngine engine, CancellationToken ct) =>
{
    if (!http.IsAdmin())
        return Results.Json(new { error = "Only an admin can do this." }, statusCode: 403);
    var info = await defs.GetLatestAsync(processKey, ct);
    if (info is null) return Results.NotFound();
    return Results.Ok(new { updated = await engine.ResolveRequestersAsync(info.Definition, processKey, ct) });
});

const long MaxDocBytes = 50L * 1024 * 1024;
var viewable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "application/pdf", "image/png", "image/jpeg", "image/gif", "text/plain" };

// The server fetches the document, so the page does not depend on the other server's frame or sign-in rules.
app.MapGet("/api/instances/{id:guid}/links/{index:int}/file", async (
    HttpContext http, Guid id, int index, EngineDb db, DefinitionClient defs,
    IHttpClientFactory factory, IConfiguration config, CancellationToken ct) =>
{
    var inst = await db.Visible(http).AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
    if (inst is null) return Results.NotFound();

    var info = await defs.GetLatestAsync(inst.ProcessKey, ct);
    var links = info?.Definition.Links ?? new List<LinkDefinition>();
    if (index < 0 || index >= links.Count) return Results.NotFound();

    var allowed = config.GetSection("Documents:AllowedHosts").Get<string[]>() ?? Array.Empty<string>();
    if (allowed.Length == 0)
        return Results.Json(new { error = "Viewing documents inside this page is switched off. An admin must list the document server under Documents:AllowedHosts." }, statusCode: 400);

    var template = links[index].Url;
    if (!LinkTemplate.HostIsFixed(template))
        return Results.Json(new { error = "The link address must start with http:// or https:// and a fixed server name." }, statusCode: 400);

    var (url, missing) = LinkTemplate.Build(template, DataBag.FromJson(inst.DataJson));
    if (missing.Count > 0)
        return Results.Json(new { error = "The link is waiting for: " + string.Join(", ", missing) }, statusCode: 400);
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        return Results.Json(new { error = "The link address is not valid." }, statusCode: 400);

    var hostPort = $"{uri.Host}:{uri.Port}";
    if (!allowed.Any(a => a.Equals(uri.Host, StringComparison.OrdinalIgnoreCase)
                          || a.Equals(hostPort, StringComparison.OrdinalIgnoreCase)))
        return Results.Json(new { error = $"The server {uri.Host} is not in Documents:AllowedHosts." }, statusCode: 400);

    HttpResponseMessage res;
    try
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        res = await factory.CreateClient("docs").SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        return Results.Json(new { error = "Could not reach the document server: " + ex.Message }, statusCode: 502);
    }

    using (res)
    {
        var code = (int)res.StatusCode;
        if (code is >= 300 and < 400)
            return Results.Json(new { error = "The document server redirected the request, usually to a sign-in page." }, statusCode: 502);
        if (code is 401 or 403)
            return Results.Json(new { error = $"The document server refused access (HTTP {code}). Try Documents:UseWindowsLogin." }, statusCode: 502);
        if (!res.IsSuccessStatusCode)
            return Results.Json(new { error = $"The document server answered HTTP {code}." }, statusCode: 502);
        if (res.Content.Headers.ContentLength > MaxDocBytes)
            return Results.Json(new { error = "The document is larger than 50 MB." }, statusCode: 413);

        await using var s = await res.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = await s.ReadAsync(buf, ct)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > MaxDocBytes)
                return Results.Json(new { error = "The document is larger than 50 MB." }, statusCode: 413);
        }

        // only types that cannot run code are shown; anything else is offered as a download
        var type = res.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        if (!viewable.Contains(type)) type = "application/octet-stream";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.File(ms.ToArray(), type);
    }
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

static async Task EnsureObjectsAsync(EngineDb db, System.Data.Common.DbConnection conn)
{
    var s = DbObjects.Schema;
    Task Do(string sql) => db.Database.ExecuteSqlRawAsync(sql);

    if (!await DbObjects.ColumnExistsAsync(conn, "PC_INSTANCES", "OBSERVERS"))
        await Do($"ALTER TABLE {s}.PC_INSTANCES ADD (OBSERVERS NVARCHAR2(2000))");
    if (!await DbObjects.ColumnExistsAsync(conn, "PC_STEPS", "ASSIGNEES"))
        await Do($"ALTER TABLE {s}.PC_STEPS ADD (ASSIGNEES NVARCHAR2(2000))");

    if (!await DbObjects.TableExistsAsync(conn, "PC_PARTICIPANTS"))
        await Do($"CREATE TABLE {s}.PC_PARTICIPANTS (ID RAW(16) NOT NULL, INSTANCE_ID RAW(16) NOT NULL, " +
                 "EMAIL NVARCHAR2(200) NOT NULL, ROLE_NAME NVARCHAR2(20) NOT NULL, CONSTRAINT PC_PARTICIPANTS_PK PRIMARY KEY (ID))");
    if (!await DbObjects.IndexExistsAsync(conn, "PC_PART_EMAIL_IX"))
        await Do($"CREATE INDEX {s}.PC_PART_EMAIL_IX ON {s}.PC_PARTICIPANTS (EMAIL, INSTANCE_ID)");
    if (!await DbObjects.IndexExistsAsync(conn, "PC_PART_INST_IX"))
        await Do($"CREATE INDEX {s}.PC_PART_INST_IX ON {s}.PC_PARTICIPANTS (INSTANCE_ID)");

    if (!await DbObjects.TableExistsAsync(conn, "PC_DASHBOARDS"))
        await Do($"CREATE TABLE {s}.PC_DASHBOARDS (ID RAW(16) NOT NULL, OWNER_EMAIL NVARCHAR2(200) NOT NULL, " +
                 "NAME NVARCHAR2(200) NOT NULL, CONFIG_JSON CLOB NOT NULL, CREATED_AT TIMESTAMP(7) NOT NULL, " +
                 "UPDATED_AT TIMESTAMP(7) NOT NULL, CONSTRAINT PC_DASHBOARDS_PK PRIMARY KEY (ID))");
    if (!await DbObjects.IndexExistsAsync(conn, "PC_DASH_OWNER_IX"))
        await Do($"CREATE INDEX {s}.PC_DASH_OWNER_IX ON {s}.PC_DASHBOARDS (OWNER_EMAIL)");
    if (!await DbObjects.ColumnExistsAsync(conn, "PC_INSTANCES", "REQUESTER"))
        await Do($"ALTER TABLE {s}.PC_INSTANCES ADD (REQUESTER NVARCHAR2(2000))");
}