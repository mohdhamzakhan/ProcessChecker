using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using NotificationService;
using Oracle.ManagedDataAccess.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<NotifyDb>(o =>
    o.UseOracle(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSingleton(builder.Configuration.GetSection("Smtp").Get<SmtpOptions>() ?? new SmtpOptions());
builder.Services.AddScoped<EmailService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
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
    var creator = db.Database.GetService<IRelationalDatabaseCreator>();
    try { creator.CreateTables(); }
    catch (OracleException ex) when (ex.Number == 955) { /* table already exists */ }

    var o = scope.ServiceProvider.GetRequiredService<SmtpOptions>();
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
app.MapPost("/api/notifications/{id:guid}/resend", async (
    Guid id, NotifyDb db, EmailService svc, CancellationToken ct) =>
{
    var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (n is null) return Results.NotFound();
    if (n.Status != NotifyStatus.Failed)
        return Results.Conflict(new { error = "Only failed emails can be resent." });

    await svc.DeliverAsync(n, ct);
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

app.Run();