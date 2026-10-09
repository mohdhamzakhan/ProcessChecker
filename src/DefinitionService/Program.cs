using System.Text.Json;
using DefinitionService;
using Microsoft.EntityFrameworkCore;
using ProcessChecker.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<DefinitionDb>(o =>
   o.UseOracle(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<DefinitionValidator>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<DefinitionDb>().Database.EnsureCreated();

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

DefinitionResponse ToResponse(DefinitionEntity e) => new(
    e.Id, e.Key, e.Name, e.Version, e.IsActive, e.CreatedAt,
    JsonSerializer.Deserialize<ProcessDefinitionDto>(e.JsonBody, json)!);

// Create a definition. Same key again = new version.
app.MapPost("/api/definitions", async (
    ProcessDefinitionDto dto, DefinitionDb db, DefinitionValidator validator) =>
{
    var errors = validator.Validate(dto);
    if (errors.Count > 0)
        return Results.BadRequest(new { errors });

    var lastVersion = await db.Definitions
        .Where(x => x.Key == dto.Key)
        .MaxAsync(x => (int?)x.Version) ?? 0;

    var entity = new DefinitionEntity
    {
        Key = dto.Key,
        Name = dto.Name,
        Version = lastVersion + 1,
        JsonBody = JsonSerializer.Serialize(dto, json)
    };

    db.Definitions.Add(entity);
    await db.SaveChangesAsync();

    return Results.Created($"/api/definitions/{entity.Key}", ToResponse(entity));
});

// List the latest version of every process
app.MapGet("/api/definitions", async (DefinitionDb db) =>
{
    var latest = await db.Definitions
        .Where(d => d.Version == db.Definitions
            .Where(x => x.Key == d.Key).Max(x => x.Version))
        .OrderBy(d => d.Name)
        .ToListAsync();

    return latest.Select(ToResponse);
});

// Latest version of one process
app.MapGet("/api/definitions/{key}", async (string key, DefinitionDb db) =>
{
    var e = await db.Definitions
        .Where(x => x.Key == key)
        .OrderByDescending(x => x.Version)
        .FirstOrDefaultAsync();

    return e is null ? Results.NotFound() : Results.Ok(ToResponse(e));
});

// All versions of one process
app.MapGet("/api/definitions/{key}/versions", async (string key, DefinitionDb db) =>
{
    var list = await db.Definitions
        .Where(x => x.Key == key)
        .OrderByDescending(x => x.Version)
        .ToListAsync();

    return list.Count == 0 ? Results.NotFound() : Results.Ok(list.Select(ToResponse));
});

// A specific version (the Engine uses this so running instances keep their version)
app.MapGet("/api/definitions/{key}/versions/{version:int}",
    async (string key, int version, DefinitionDb db) =>
    {
        var e = await db.Definitions
            .FirstOrDefaultAsync(x => x.Key == key && x.Version == version);

        return e is null ? Results.NotFound() : Results.Ok(ToResponse(e));
    });

// Lookup by id
app.MapGet("/api/definitions/by-id/{id:guid}", async (Guid id, DefinitionDb db) =>
{
    var e = await db.Definitions.FindAsync(id);
    return e is null ? Results.NotFound() : Results.Ok(ToResponse(e));
});

// Enable / disable a process (all versions)
app.MapPatch("/api/definitions/{key}/active", async (string key, bool value, DefinitionDb db) =>
{
    var list = await db.Definitions.Where(x => x.Key == key).ToListAsync();
    if (list.Count == 0) return Results.NotFound();

    list.ForEach(x => x.IsActive = value);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// Lets the React designer know what is supported
app.MapGet("/api/meta", () => new
{
    stepTypes = StepTypes.All,
    fieldTypes = FieldTypes.All,
    triggerTypes = new[] { TriggerTypes.Manual, TriggerTypes.DatabasePoll }
});

app.Run();

public record DefinitionResponse(
    Guid Id, string Key, string Name, int Version,
    bool IsActive, DateTime CreatedAt, ProcessDefinitionDto Definition);