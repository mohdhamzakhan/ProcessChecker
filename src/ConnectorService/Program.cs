using System.Text.Json;
using ConnectorService;
using ConnectorService.Connectors;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Oracle.ManagedDataAccess.Client;
using ProcessChecker.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ConnectorDb>(o =>
    o.UseOracle(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddDataProtection()
    .SetApplicationName("ProcessChecker")
    .PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["KeysPath"] ?? "keys"));

builder.Services.AddSingleton<IConnector, OracleConnector>();
builder.Services.AddSingleton<IConnector, SqlServerConnector>();
builder.Services.AddSingleton<IConnector, MySqlConnectorImpl>();
builder.Services.AddSingleton<IConnector, PostgreSqlConnector>();
builder.Services.AddSingleton<IConnector, AccessConnector>();
builder.Services.AddSingleton<IConnector, JsonFileConnector>();
builder.Services.AddSingleton<IConnector, CsvFileConnector>();
builder.Services.AddSingleton<ConnectorRegistry>();
builder.Services.AddScoped<ConnectionStore>();


builder.Services.AddMemoryCache();
builder.Services.AddScoped<SchemaResolver>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseInternalKey();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();

// EnsureCreated() skips tables when the schema already has some (DefinitionService
// created its table first), so create this service's table explicitly.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ConnectorDb>();
    await db.Database.OpenConnectionAsync();
    try
    {
        var conn = db.Database.GetDbConnection();
        if (!await DbObjects.TableExistsAsync(conn, "PC_CONNECTIONS"))
            db.Database.GetService<IRelationalDatabaseCreator>().CreateTables();
        else
        {
            var s = DbObjects.Schema;
            if (!await DbObjects.ColumnExistsAsync(conn, "PC_CONNECTIONS", "DEFAULT_SCHEMA"))
                await db.Database.ExecuteSqlRawAsync($"ALTER TABLE {s}.PC_CONNECTIONS ADD (DEFAULT_SCHEMA NVARCHAR2(128))");
            if (!await DbObjects.ColumnExistsAsync(conn, "PC_CONNECTIONS", "ALLOWED_SCHEMAS"))
                await db.Database.ExecuteSqlRawAsync($"ALTER TABLE {s}.PC_CONNECTIONS ADD (ALLOWED_SCHEMAS NVARCHAR2(2000))");
        }
    }
    finally { await db.Database.CloseConnectionAsync(); }
}

static Dictionary<string, object?>? ToParams(Dictionary<string, JsonElement>? p) =>
    p?.ToDictionary(kv => kv.Key, kv => kv.Value.ValueKind switch
    {
        JsonValueKind.String => (object?)kv.Value.GetString(),
        JsonValueKind.Number => kv.Value.TryGetInt64(out var l) ? (object)l : kv.Value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    });

// ---- What the UI needs to build the form ----
app.MapGet("/api/connection-types", () => new[]
{
    new TypeDescriptor("Oracle", "Oracle Database", "server",
        "User Id=USER;Password=PWD;Data Source=host:1521/SERVICE",
        "SELECT only. Parameters like :id"),
    new TypeDescriptor("SqlServer", "SQL Server", "server",
        "Server=host;Database=DB;User Id=USER;Password=PWD;TrustServerCertificate=True",
        "SELECT only. Parameters like :id"),
    new TypeDescriptor("MySql", "MySQL", "server",
        "Server=host;Port=3306;Database=DB;User=USER;Password=PWD",
        "SELECT only. Parameters like :id"),
    new TypeDescriptor("PostgreSql", "PostgreSQL", "server",
        "Host=host;Port=5432;Database=DB;Username=USER;Password=PWD",
        "SELECT only. Parameters like :id"),
    new TypeDescriptor("Access", "Microsoft Access (.mdb/.accdb)", "file",
        @"C:\ProcessData\orders.accdb",
        "SELECT only. Parameters like :id"),
    new TypeDescriptor("Json", "JSON file", "file",
        @"C:\ProcessData\requests.json",
        "JSONPath, e.g. $.requests[?(@.status=='new')]. Empty = whole file"),
    new TypeDescriptor("Csv", "CSV / flat file", "file",
        @"C:\ProcessData\requests.csv",
        "Filter, e.g. status = new AND amount > 1000. Empty = all rows")
});

// ---- Manage connections ----
app.MapGet("/api/connections", async (ConnectorDb db, ConnectionStore store) =>
    (await db.Connections.OrderBy(x => x.Name).ToListAsync()).Select(store.ToInfo));

app.MapGet("/api/connections/{name}", async (string name, ConnectionStore store) =>
{
    var e = await store.GetEntityAsync(name);
    return e is null ? Results.NotFound() : Results.Ok(store.ToInfo(e));
});

app.MapPost("/api/connections", async (ConnectionRequest req, ConnectionStore store) =>
{
    var errors = store.Validate(req, isUpdate: false);
    if (errors.Count > 0) return Results.BadRequest(new { errors });
    if (await store.GetEntityAsync(req.Name) is not null)
        return Results.Conflict(new { error = $"Connection '{req.Name}' already exists." });

    var e = await store.CreateAsync(req);
    return Results.Created($"/api/connections/{e.Name}", store.ToInfo(e));
});

app.MapPut("/api/connections/{name}", async (string name, ConnectionRequest req, ConnectionStore store) =>
{
    var fixedReq = req with { Name = name };      // renaming is not supported
    var errors = store.Validate(fixedReq, isUpdate: true);
    if (errors.Count > 0) return Results.BadRequest(new { errors });

    var e = await store.UpdateAsync(name, fixedReq);
    return e is null ? Results.NotFound() : Results.Ok(store.ToInfo(e));
});

app.MapDelete("/api/connections/{name}", async (string name, ConnectionStore store) =>
    await store.DeleteAsync(name) ? Results.NoContent() : Results.NotFound());

// ---- Test ----
app.MapPost("/api/connections/test", async (
    ConnectionRequest req, ConnectionStore store, ConnectorRegistry reg, CancellationToken ct) =>
{
    var errors = store.Validate(req, isUpdate: false);
    if (errors.Count > 0) return Results.BadRequest(new { errors });
    return Results.Ok(await reg.Get(req.Type).TestAsync(store.ResolveRequest(req), ct));
});

app.MapPost("/api/connections/{name}/test", async (
    string name, ConnectionStore store, ConnectorRegistry reg, CancellationToken ct) =>
{
    var e = await store.GetEntityAsync(name);
    return e is null
        ? Results.NotFound()
        : Results.Ok(await reg.Get(e.Type).TestAsync(store.Resolve(e), ct));
});

// ---- Browse tables / run queries ----
app.MapGet("/api/connections/{name}/tables", async (
    string name, ConnectionStore store, ConnectorRegistry reg, CancellationToken ct) =>
{
    var e = await store.GetEntityAsync(name);
    if (e is null) return Results.NotFound();
    try { return Results.Ok(await reg.Get(e.Type).GetTablesAsync(store.Resolve(e), ct)); }
    catch (Exception ex) when (ex is not OperationCanceledException)
    { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPost("/api/connections/{name}/query", async (
    string name, QueryRequest req, ConnectionStore store, ConnectorRegistry reg, SchemaResolver schemas,
    IConfiguration cfg, CancellationToken ct) =>
{
    var e = await store.GetEntityAsync(name);
    if (e is null) return Results.NotFound();
    if (!e.IsActive) return Results.BadRequest(new { error = "Connection is disabled." });

    var max = Math.Clamp(req.MaxRows ?? 100, 1, cfg.GetValue("MaxRows", 5000));
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(60));

    try
    {
        var connector = reg.Get(e.Type);
        var rc = store.Resolve(e);
        rc = rc with { Schema = await schemas.ResolveAsync(e, rc, connector, req.Schema, cts.Token) };

        var res = await connector.QueryAsync(rc, req.Query, ToParams(req.Parameters), max, cts.Token);
        return Results.Ok(new { res.Columns, res.Rows, res.Truncated, Count = res.Rows.Count, schema = rc.Schema });
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// ---- Schema browsing ----
app.MapGet("/api/connections/{name}/schemas", async (
    string name, ConnectionStore store, ConnectorRegistry reg, SchemaResolver schemas, CancellationToken ct) =>
{
    var e = await store.GetEntityAsync(name);
    if (e is null) return Results.NotFound();
    try
    {
        var list = await schemas.AvailableAsync(e, store.Resolve(e), reg.Get(e.Type), ct);
        return Results.Ok(new
        {
            schemas = list,
            defaultSchema = e.DefaultSchema,
            restricted = !string.IsNullOrWhiteSpace(e.AllowedSchemas)
        });
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapGet("/api/connections/{name}/schemas/{schema}/tables", async (
    string name, string schema, ConnectionStore store, ConnectorRegistry reg,
    SchemaResolver schemas, CancellationToken ct) =>
{
    var e = await store.GetEntityAsync(name);
    if (e is null) return Results.NotFound();
    try
    {
        var connector = reg.Get(e.Type);
        var rc = store.Resolve(e);
        var s = await schemas.ResolveAsync(e, rc, connector, schema, ct);
        return Results.Ok(await ((ISchemaConnector)connector).GetSchemaTablesAsync(rc, s!, ct));
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapGet("/api/connections/{name}/schemas/{schema}/tables/{table}/columns", async (
    string name, string schema, string table, ConnectionStore store, ConnectorRegistry reg,
    SchemaResolver schemas, CancellationToken ct) =>
{
    var e = await store.GetEntityAsync(name);
    if (e is null) return Results.NotFound();
    try
    {
        var connector = reg.Get(e.Type);
        var rc = store.Resolve(e);
        var s = await schemas.ResolveAsync(e, rc, connector, schema, ct);
        return Results.Ok(await ((ISchemaConnector)connector).GetColumnsAsync(rc, s!, table, ct));
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    { return Results.BadRequest(new { error = ex.Message }); }
});

app.Run();