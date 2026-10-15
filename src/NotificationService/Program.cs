using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using NotificationService;
using Oracle.ManagedDataAccess.Client;
using ProcessChecker.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddDockerSecrets();
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 60L * 1024 * 1024);
builder.Services.AddHostedService<RetentionWorker>();
builder.Services.AddDbContext<NotifyDb>(o => o.UseOracle(builder.Configuration.GetConnectionString("Default"),
    x => x.MigrationsHistoryTable(DbMigrator.HistoryTable("NOTIFY"), DbObjects.Schema)));
builder.Services.AddSingleton(builder.Configuration.GetSection("Smtp").Get<SmtpOptions>() ?? new SmtpOptions());
builder.Services.AddScoped<EmailService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
SecretCheck.Run(app.Configuration, app.Environment, app.Logger,
    ("Gateway:Key", 24), ("ConnectionStrings:Default", 10));
app.UseInternalKey();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();

app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (NotifyException ex)
    {
        ctx.Response.StatusCode = ex.Status;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message, errors = ex.Errors });
    }
});

// Create only this service's table (the schema already has other services' tables)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NotifyDb>();
    await DbMigrator.RunAsync(db, "PC_NOTIFICATIONS", async conn =>
    {
        if (!await DbObjects.ColumnExistsAsync(conn, "PC_NOTIFICATIONS", "ATTACHMENTS"))
            await db.Database.ExecuteSqlRawAsync($"ALTER TABLE {DbObjects.Schema}.PC_NOTIFICATIONS ADD (ATTACHMENTS NVARCHAR2(1000))");
    }, app.Logger);

    var o = scope.ServiceProvider.GetRequiredService<SmtpOptions>();

    if (!MimeKit.MailboxAddress.TryParse(o.FromEmail, out _))
        throw new InvalidOperationException($"Smtp:FromEmail '{o.FromEmail}' is not a valid email address.");
    app.Logger.LogInformation("Email mode: {Mode} (host {Host}:{Port})",
        o.DryRun ? "DRY RUN, nothing is sent" : "LIVE", o.Host, o.Port);
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

// ---- The engine calls this ----
app.MapPost("/api/notifications/email", async (EmailRequest req, EmailService svc, CancellationToken ct) =>
{
    var log = await svc.SendAsync(req, ct);

    return log.Status == NotifyStatus.Failed
        ? Results.Json(new { id = log.Id, status = log.Status, error = log.Error }, statusCode: 502)
        : Results.Ok(new { id = log.Id, status = log.Status });
});

// ---- Send log ----
app.MapGet("/api/notifications", async (
    NotifyDb db, string? status, string? q, int? page, int? pageSize, CancellationToken ct) =>
{
    IQueryable<NotificationLog> query = db.Notifications.AsNoTracking();
    if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
    if (!string.IsNullOrWhiteSpace(q))
        query = query.Where(x => x.Subject.Contains(q) || x.ToList.Contains(q));

    var size = Math.Clamp(pageSize ?? 25, 1, 100);
    var p = Math.Max(page ?? 1, 1);
    var total = await query.CountAsync(ct);

    // the body is left out of the list (it can be large), use GET by id for the full email
    var items = await query.OrderByDescending(x => x.CreatedAt)
        .Skip((p - 1) * size).Take(size)
        .Select(x => new
        {
            x.Id,
            x.ToList,
            x.Subject,
            x.IsHtml,
            x.Status,
            x.Error,
            x.Attempts,
            x.CreatedAt,
            x.AttachmentNames,
            x.SentAt
        })
        .ToListAsync(ct);

    return Results.Ok(new { total, page = p, pageSize = size, items });
});

app.MapGet("/api/notifications/{id:guid}", async (Guid id, NotifyDb db, CancellationToken ct) =>
{
    var n = await db.Notifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    return n is null ? Results.NotFound() : Results.Ok(n);
});

// Try a failed email again
app.MapPost("/api/notifications/{id:guid}/resend", async (Guid id, NotifyDb db, EmailService svc, CancellationToken ct) =>
{
    var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (n is null) return Results.NotFound();
    if (n.Status != NotifyStatus.Failed) return Results.Conflict(new { error = "Only failed emails can be resent." });
    if (n.AttachmentNames != null)
        return Results.Conflict(new { error = "This email had attachments, which are not stored. Retry the step in the request instead." });

    await svc.DeliverAsync(n, null, ct);
    return n.Status == NotifyStatus.Failed
        ? Results.Json(new { id = n.Id, status = n.Status, error = n.Error }, statusCode: 502)
        : Results.Ok(new { id = n.Id, status = n.Status });
});
// Check your SMTP settings
app.MapPost("/api/notifications/test", async (TestRequest req, EmailService svc, CancellationToken ct) =>
{
    var log = await svc.SendAsync(new EmailRequest(
        new[] { req.To },
        "ProcessChecker test email",
        "<p>If you can read this, NotificationService is configured correctly.</p>",
        true), ct);

    return log.Status == NotifyStatus.Failed
        ? Results.Json(new { id = log.Id, status = log.Status, error = log.Error }, statusCode: 502)
        : Results.Ok(new { id = log.Id, status = log.Status });
});
app.MapGet("/api/notifications/stats", async (NotifyDb db, IConfiguration cfg, CancellationToken ct) =>
{
    var by = await db.Notifications.AsNoTracking().GroupBy(x => x.Status)
        .Select(g => new { status = g.Key, count = g.Count() }).ToListAsync(ct);
    var oldest = await db.Notifications.AsNoTracking().OrderBy(x => x.CreatedAt)
        .Select(x => (DateTime?)x.CreatedAt).FirstOrDefaultAsync(ct);
    return Results.Ok(new
    {
        total = by.Sum(x => x.count),
        byStatus = by.ToDictionary(x => x.status, x => x.count),
        oldest,
        retentionDays = cfg.GetValue("Retention:Days", 90),
        failedRetentionDays = cfg.GetValue("Retention:FailedDays", 180)
    });
});

app.MapPost("/api/notifications/cleanup", async (NotifyDb db, IConfiguration cfg, int? days, int? failedDays, CancellationToken ct) =>
{
    var d = days ?? cfg.GetValue("Retention:Days", 90);
    var f = failedDays ?? cfg.GetValue("Retention:FailedDays", 180);
    if (d < 1 && f < 1) return Results.BadRequest(new { error = "Give a number of days of at least 1." });
    return Results.Ok(new { removed = await LogRetention.RunAsync(db, d, f, ct) });
});

app.Run();