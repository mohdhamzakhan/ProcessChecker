using System.Security.Cryptography;
using DocumentService;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Oracle.ManagedDataAccess.Client;
using ProcessChecker.Contracts;

var builder = WebApplication.CreateBuilder(args);

var opt = builder.Configuration.GetSection("Storage").Get<DocOptions>() ?? new DocOptions();
if (opt.AllowedExtensions.Length == 0) opt.AllowedExtensions = FileRules.DefaultExtensions;
opt.AllowedExtensions = opt.AllowedExtensions
    .Select(e => e.Trim().ToLowerInvariant()).Where(FileRules.IsKnown).Distinct().ToArray();

var maxBytes = opt.MaxSizeMb * 1024L * 1024L;
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = maxBytes + 1024 * 1024);
builder.Services.Configure<FormOptions>(f => f.MultipartBodyLengthLimit = maxBytes + 1024 * 1024);

builder.Services.AddSingleton(opt);
builder.Services.AddSingleton<DocStorage>();
builder.Services.AddDbContext<DocumentDb>(o => o.UseOracle(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseInternalKey();
app.UseSwagger();
app.UseSwaggerUI();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DocumentDb>();
    var creator = db.Database.GetService<IRelationalDatabaseCreator>();
    try { creator.CreateTables(); }
    catch (OracleException ex) when (ex.Number == 955) { /* table already exists */ }
    app.Logger.LogInformation("Storing files in {Root} (max {Mb} MB; types: {Types})",
        scope.ServiceProvider.GetRequiredService<DocStorage>().Root, opt.MaxSizeMb,
        string.Join(" ", opt.AllowedExtensions));
}

static DocumentInfo ToInfo(DocumentEntity d) => new(
    d.Id, d.FileName, d.ContentType, d.SizeBytes, d.InstanceId, d.StepKey, d.UploadedBy, d.UploadedAt);

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

// ---- Upload: multipart/form-data with a "file" field (+ optional instanceId, stepKey) ----
app.MapPost("/api/documents", async (
    HttpContext http, DocumentDb db, DocStorage storage, DocOptions options, CancellationToken ct) =>
{
    if (!http.Request.HasFormContentType)
        return Results.BadRequest(new { error = "Send multipart/form-data with a field named 'file'." });

    var form = await http.Request.ReadFormAsync(ct);
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "No file was received (or it is empty)." });

    var max = options.MaxSizeMb * 1024L * 1024L;
    if (file.Length > max)
        return Results.Json(new { error = $"The file is larger than {options.MaxSizeMb} MB." }, statusCode: 413);

    var name = FileRules.CleanName(file.FileName);
    var ext = Path.GetExtension(name).ToLowerInvariant();
    if (!options.AllowedExtensions.Contains(ext))
        return Results.BadRequest(new
        {
            error = $"File type '{(ext.Length == 0 ? "(none)" : ext)}' is not allowed. Allowed: {string.Join(", ", options.AllowedExtensions)}"
        });

    var head = new byte[8];
    await using (var probe = file.OpenReadStream())
    {
        var n = await probe.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
        Array.Resize(ref head, n);
    }
    if (!FileRules.HeaderMatches(ext, head))
        return Results.BadRequest(new { error = $"The content of the file does not look like a real {ext} file." });

    Guid? instanceId = Guid.TryParse(form["instanceId"].ToString(), out var g) ? g : null;
    var stepKey = form["stepKey"].ToString().Trim();
    if (stepKey.Length > 100) stepKey = stepKey[..100];

    var id = Guid.NewGuid();
    var (rel, full) = storage.NewPath(id);
    long total = 0;
    string hash;

    try
    {
        using var sha = SHA256.Create();
        await using var input = file.OpenReadStream();
        await using var output = new FileStream(full, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 81920, useAsync: true);
        var buf = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buf, ct)) > 0)
        {
            sha.TransformBlock(buf, 0, read, null, 0);
            await output.WriteAsync(buf.AsMemory(0, read), ct);
            total += read;
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        hash = Convert.ToHexString(sha.Hash!);
    }
    catch { TryDelete(full); throw; }

    if (total > max)
    {
        TryDelete(full);
        return Results.Json(new { error = $"The file is larger than {options.MaxSizeMb} MB." }, statusCode: 413);
    }

    var doc = new DocumentEntity
    {
        Id = id,
        FileName = name,
        StoredPath = rel,
        ContentType = FileRules.ContentTypeOf(ext),
        SizeBytes = total,
        Sha256 = hash,
        InstanceId = instanceId,
        StepKey = stepKey.Length > 0 ? stepKey : null,
        UploadedBy = http.Caller() ?? "unknown"
    };

    try
    {
        db.Documents.Add(doc);
        await db.SaveChangesAsync(ct);
    }
    catch { TryDelete(full); throw; }

    return Results.Created($"/api/documents/{id}",
        new { fileId = id, fileName = name, size = total, contentType = doc.ContentType });

    static void TryDelete(string path) { try { File.Delete(path); } catch { /* ignore */ } }
}).WithName("UploadDocument");

// ---- Download (always as an attachment, never rendered by the browser) ----
app.MapGet("/api/documents/{id:guid}/download", async (
    Guid id, HttpContext http, DocumentDb db, DocStorage storage, CancellationToken ct) =>
{
    var d = await db.Documents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    if (d is null) return Results.NotFound(new { error = "Document not found." });

    var path = storage.Resolve(d.StoredPath);
    if (!File.Exists(path))
        return Results.NotFound(new { error = "The file is missing on the server." });

    http.Response.Headers["X-Content-Type-Options"] = "nosniff";
    return Results.File(path, d.ContentType, d.FileName, enableRangeProcessing: true);
});

app.MapGet("/api/documents/{id:guid}", async (Guid id, DocumentDb db, CancellationToken ct) =>
{
    var d = await db.Documents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    return d is null ? Results.NotFound() : Results.Ok(ToInfo(d));
});

// Files of one request. Listing everything is for admins only.
app.MapGet("/api/documents", async (HttpContext http, DocumentDb db, Guid? instanceId, CancellationToken ct) =>
{
    if (instanceId is null && !http.IsAdmin())
        return Results.Json(new { error = "instanceId is required." }, statusCode: 403);

    IQueryable<DocumentEntity> q = db.Documents.AsNoTracking();
    if (instanceId != null) q = q.Where(x => x.InstanceId == instanceId);

    var items = await q.OrderByDescending(x => x.UploadedAt).Take(200).ToListAsync(ct);
    return Results.Ok(items.Select(ToInfo));
});

// Delete: the uploader or an admin
app.MapDelete("/api/documents/{id:guid}", async (
    Guid id, HttpContext http, DocumentDb db, DocStorage storage, CancellationToken ct) =>
{
    var d = await db.Documents.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (d is null) return Results.NotFound();

    var mine = string.Equals(d.UploadedBy, http.Caller(), StringComparison.OrdinalIgnoreCase);
    if (!http.IsAdmin() && !mine)
        return Results.Json(new { error = "Only the uploader or an admin can delete this file." }, statusCode: 403);

    db.Documents.Remove(d);
    await db.SaveChangesAsync(ct);
    try { File.Delete(storage.Resolve(d.StoredPath)); } catch { /* row is gone, file cleanup can be retried by hand */ }
    return Results.NoContent();
});

app.Run();