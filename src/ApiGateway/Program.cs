using ApiGateway;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Oracle.ManagedDataAccess.Client;
using ProcessChecker.Contracts;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Yarp.ReverseProxy.Transforms;
using static ApiGateway.UserEntity;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddDockerSecrets();
var cfg = builder.Configuration;

var jwtKey = cfg["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
    throw new InvalidOperationException("Jwt:Key is missing or shorter than 32 characters. Set it with user-secrets.");
var gatewayKey = cfg["Gateway:Key"];
if (string.IsNullOrWhiteSpace(gatewayKey) || gatewayKey.Length < 24)
    throw new InvalidOperationException("Gateway:Key is missing or shorter than 24 characters. Set the Gateway__Key environment variable.");

builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 25 * 1024 * 1024);

builder.Services.AddMemoryCache();
builder.Services.AddDbContext<GatewayDb>(o => o.UseOracle(builder.Configuration.GetConnectionString("Default"),
    x => x.MigrationsHistoryTable(DbMigrator.HistoryTable("GATEWAY"), DbObjects.Schema)));
builder.Services.AddSingleton<TokenService>();
var adOptions = cfg.GetSection("Ad").Get<AdOptions>() ?? new AdOptions();
builder.Services.AddSingleton(adOptions);
builder.Services.AddSingleton<AdService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "ProcessChecker",
            ValidateAudience = true,
            ValidAudience = "ProcessChecker",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "email",
            RoleClaimType = "role"
        };
        o.Events = new JwtBearerEvents
        {
            // Every request re-checks the user (cached for 60 s): disabling a user or changing a role takes effect quickly.
            OnTokenValidated = async ctx =>
            {
                if (!Guid.TryParse(ctx.Principal?.FindFirst("sub")?.Value, out var id))
                {
                    ctx.Fail("Invalid token.");
                    return;
                }
                var cache = ctx.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
                var info = await cache.GetOrCreateAsync<UserInfo?>($"user:{id}", async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
                    var db = ctx.HttpContext.RequestServices.GetRequiredService<GatewayDb>();
                    return await db.Users.AsNoTracking().Where(u => u.Id == id)
                        .Select(u => new UserInfo(u.Email, u.Role, u.IsActive)).FirstOrDefaultAsync();
                });
                if (info is null || !info.IsActive)
                {
                    ctx.Fail("User is disabled.");
                    return;
                }
                var identity = (ClaimsIdentity)ctx.Principal!.Identity!;
                foreach (var c in identity.FindAll("role").ToList()) identity.RemoveClaim(c);
                identity.AddClaim(new Claim("role", info.Role));      // role always comes from the database
            }
        };
    });

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("authenticated", p => p.RequireAuthenticatedUser());
    o.AddPolicy("admin", p => p.RequireAuthenticatedUser().RequireRole(Roles.Admin));
});

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(cfg.GetSection("ReverseProxy"))
    .AddTransforms(ctx => ctx.AddRequestTransform(t =>
    {
        var h = t.ProxyRequest.Headers;

        // Never trust identity headers sent by the browser, always rebuild them
        h.Remove(InternalAuth.KeyHeader);
        h.Remove(InternalAuth.UserHeader);
        h.Remove(InternalAuth.RoleHeader);
        h.Remove(InternalAuth.NameHeader);
        h.Remove(InternalAuth.GroupsHeader);
        h.TryAddWithoutValidation(InternalAuth.KeyHeader, gatewayKey);

        var u = t.HttpContext.User;
        if (u.Identity?.IsAuthenticated == true)
        {
            var grp = u.FindAll("grp").Select(c => c.Value).ToList();
            if (grp.Count > 0) 
                h.TryAddWithoutValidation(InternalAuth.GroupsHeader, string.Join(",", grp));
            h.TryAddWithoutValidation(InternalAuth.UserHeader, u.FindFirst("email")?.Value ?? "");
            h.TryAddWithoutValidation(InternalAuth.RoleHeader, u.FindFirst("role")?.Value ?? "");
            h.TryAddWithoutValidation(InternalAuth.NameHeader, u.FindFirst("uname")?.Value ?? "");
        }
        return ValueTask.CompletedTask;
    }));

var app = builder.Build();
SecretCheck.Run(app.Configuration, app.Environment, app.Logger,
    ("Gateway:Key", 24), ("ConnectionStrings:Default", 10), ("Jwt:Key", 32));

// Create the users table and the first admin
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GatewayDb>();
    await DbMigrator.RunAsync(db, "PC_USERS", async conn =>
    {
        if (!await DbObjects.ColumnExistsAsync(conn, "PC_USERS", "AUTH_SOURCE"))
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE {DbObjects.Schema}.PC_USERS ADD (AUTH_SOURCE NVARCHAR2(10) DEFAULT 'Local' NOT NULL)");
    }, app.Logger);

    if (!await db.Users.AnyAsync())
    {
        var email = cfg["Auth:AdminEmail"]?.Trim().ToLowerInvariant();
        var pw = cfg["Auth:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || Passwords.Validate(pw) != null)
            app.Logger.LogWarning("No users exist and Auth:AdminEmail / Auth:AdminPassword (min 10 chars) are not set, so nobody can log in.");
        else
        {
            db.Users.Add(new UserEntity
            {
                Email = email,
                DisplayName = cfg["Auth:AdminName"] ?? "Administrator",
                PasswordHash = Passwords.Hash(pw!),
                Role = Roles.Admin
            });
            await db.SaveChangesAsync();
            app.Logger.LogInformation("Created the first admin user {Email}.", email);
        }
    }
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

static object Dto(UserEntity u) => new
{
    id = u.Id,
    email = u.Email,
    displayName = u.DisplayName,
    role = u.Role,
    authSource = u.AuthSource,
    isActive = u.IsActive,
    createdAt = u.CreatedAt
};

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

// ---------------------------------------------------------------- login
app.MapPost("/api/auth/login", async (
    LoginRequest req, GatewayDb db, TokenService tokens, AdService ad, AdOptions adOpt,
    IMemoryCache cache, ILogger<Program> log) =>
{
    var input = (req.Email ?? "").Trim();
    var password = req.Password ?? "";
    var key = input.ToLowerInvariant();

    UserEntity? user = null;
    string? sam = null;
    List<string>? groups = null;
    var ok = false;

    var local = await db.Users.FirstOrDefaultAsync(u => u.Email == key && u.AuthSource == AuthSources.Local);
    if (local != null)
    {
        user = local;
        ok = Passwords.Verify(password, local.PasswordHash);
    }
    else if (ad.Enabled)
    {
        AdUser? a;
        try { a = await Task.Run(() => ad.Authenticate(input, password)); }
        catch (AdUnavailableException ex)
        {
            log.LogError(ex, "Active Directory is not reachable");
            return Results.Json(new { error = "The company directory is not reachable. Try again, or ask an admin." }, statusCode: 503);
        }
        if (a != null)
        {
            user = await UpsertAdUser(db, a, adOpt);
            if (user != null) cache.Remove($"user:{user.Id}");
            sam = a.Username; groups = a.Groups;
            ok = user != null;
        }
    }
    else Passwords.Verify(password, null);

    if (user is null || !ok || !user.IsActive)
        return Results.Json(new { error = "Invalid email or password." }, statusCode: 401);

    var (token, expires) = tokens.Create(user, sam, groups);
    return Results.Ok(new
    {
        token,
        expiresAt = expires,
        user = new { email = user.Email, name = user.DisplayName, role = user.Role, username = sam ?? user.Email, groups = groups ?? new List<string>() }
    });
}).RequireRateLimiting("login");

// Creates the user on first AD login. When Ad:AdminGroupDn is set, the role always follows that group.
static async Task<UserEntity?> UpsertAdUser(GatewayDb db, AdUser a, AdOptions o)
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == a.Email);
    if (user is null)
    {
        user = new UserEntity
        {
            Email = a.Email,
            DisplayName = a.DisplayName,
            PasswordHash = "!",   // no local password
            AuthSource = AuthSources.Ad,
            Role = a.IsAdmin ? Roles.Admin : Roles.User
        };
        db.Users.Add(user);
    }
    else if (user.AuthSource != AuthSources.Ad)
    {
        return null;     // the same email belongs to a local account: refuse instead of taking it over
    }
    else
    {
        user.DisplayName = a.DisplayName;
        if (o.AdminGroups.Length > 0)
            user.Role = a.IsAdmin ? Roles.Admin : Roles.User;
    }
    await db.SaveChangesAsync();
    return user;
}

app.MapGet("/api/auth/me", (ClaimsPrincipal u) => Results.Ok(new
{
    email = u.FindFirst("email")?.Value,
    name = u.FindFirst("name")?.Value,
    username = u.FindFirst("uname")?.Value,
    groups = u.FindAll("grp").Select(c => c.Value).ToList(),
    role = u.FindFirst("role")?.Value
})).RequireAuthorization("authenticated");

app.MapPost("/api/auth/change-password", async (ClaimsPrincipal u, ChangePasswordRequest r, GatewayDb db) =>
{

    var user = await db.Users.FindAsync(Guid.Parse(u.FindFirst("sub")!.Value));
    if (user is null) return Results.NotFound();
    if (user.AuthSource == AuthSources.Ad)
        return Results.BadRequest(new { error = "Your password is managed by your company directory." });
    if (!Passwords.Verify(r.CurrentPassword ?? "", user.PasswordHash))
        return Results.BadRequest(new { error = "Your current password is wrong." });
    if (Passwords.Validate(r.NewPassword) is { } problem)
        return Results.BadRequest(new { error = problem });

    user.PasswordHash = Passwords.Hash(r.NewPassword!);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization("authenticated");

// ---------------------------------------------------------------- user admin
var admin = app.MapGroup("/api/auth/users").RequireAuthorization("admin");

admin.MapGet("", async (GatewayDb db) =>
    (await db.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync()).Select(Dto));

admin.MapPost("", async (CreateUserRequest r, GatewayDb db) =>
{
    var errors = new List<string>();
    var email = (r.Email ?? "").Trim().ToLowerInvariant();
    if (!MailAddress.TryCreate(email, out var addr) || addr.Address != email)
        errors.Add("A valid email is required.");
    if (string.IsNullOrWhiteSpace(r.DisplayName)) errors.Add("Name is required.");
    if (Passwords.Validate(r.Password) is { } p) errors.Add(p);
    var role = r.Role ?? Roles.User;
    if (!Roles.All.Contains(role)) errors.Add($"Role must be one of: {string.Join(", ", Roles.All)}.");
    if (errors.Count > 0) return Results.BadRequest(new { error = "Validation failed.", errors });

    if (await db.Users.AnyAsync(u => u.Email == email))
        return Results.Conflict(new { error = $"{email} already exists." });

    var user = new UserEntity
    {
        Email = email,
        DisplayName = r.DisplayName!.Trim(),
        PasswordHash = Passwords.Hash(r.Password!),
        Role = role
    };
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return Results.Created($"/api/auth/users/{user.Id}", Dto(user));
});

admin.MapPatch("/{id:guid}", async (
    Guid id, ClaimsPrincipal me, UpdateUserRequest r, GatewayDb db, IMemoryCache cache) =>
{
    var user = await db.Users.FindAsync(id);
    if (user is null) return Results.NotFound();

    var isSelf = string.Equals(me.FindFirst("sub")?.Value, id.ToString(), StringComparison.OrdinalIgnoreCase);
    if (isSelf && (r.IsActive == false || (r.Role != null && r.Role != user.Role)))
        return Results.BadRequest(new { error = "You cannot disable or demote yourself." });
    if (r.NewPassword != null && user.AuthSource == AuthSources.Ad)
        return Results.BadRequest(new { error = "This user signs in with Active Directory." });

    if (r.Role != null)
    {
        if (!Roles.All.Contains(r.Role))
            return Results.BadRequest(new { error = $"Role must be one of: {string.Join(", ", Roles.All)}." });
        user.Role = r.Role;
    }
    if (r.IsActive != null) user.IsActive = r.IsActive.Value;
    if (r.NewPassword != null)
    {
        if (Passwords.Validate(r.NewPassword) is { } problem)
            return Results.BadRequest(new { error = problem });
        user.PasswordHash = Passwords.Hash(r.NewPassword);
    }

    await db.SaveChangesAsync();
    cache.Remove($"user:{id}");
    return Results.Ok(Dto(user));
});

app.MapGet("/api/auth/directory", async (string? q, GatewayDb db, AdService ad, ILogger<Program> log) =>
{
    var term = (q ?? "").Trim();
    if (term.Length < 2) return Results.Ok(new { items = Array.Empty<DirItem>(), warning = (string?)null });

    var like = term.ToLowerInvariant();
    var local = await db.Users.AsNoTracking()
        .Where(u => u.IsActive && (u.Email.Contains(like) || u.DisplayName.ToLower().Contains(like)))
        .OrderBy(u => u.Email).Take(15).Select(u => new { u.Email, u.DisplayName, u.AuthSource }).ToListAsync();

    var items = local.Select(u => new DirItem(u.Email, u.DisplayName, u.Email, u.AuthSource, "user")).ToList();
    string? warning = null;

    if (ad.Enabled)
    {
        try
        {
            foreach (var p in await Task.Run(() => ad.Search(term)))
                if (!items.Any(i => i.email == p.Email)) items.Add(new DirItem(p.Email, p.Name, p.Username, "AD", "user"));
            foreach (var g in await Task.Run(() => ad.SearchGroups(term)))
                items.Add(new DirItem(g.Token, g.Name, g.Sam, "AD group", "group"));
        }
        catch (AdUnavailableException ex)
        {
            log.LogWarning(ex, "Directory search failed");
            warning = "The company directory could not be searched. Local users and typed addresses still work.";
        }
    }
    return Results.Ok(new { items = items.OrderBy(i => i.kind).ThenBy(i => i.name).Take(25), warning });
}).RequireAuthorization("admin");

app.MapPost("/internal/identities/resolve", async (HttpContext http, ResolveRequest r, AdService ad, IMemoryCache cache) =>
{
    var given = Encoding.UTF8.GetBytes(http.Request.Headers[InternalAuth.KeyHeader].ToString());
    var expected = Encoding.UTF8.GetBytes(gatewayKey!);
    if (given.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(given, expected))
        return Results.Json(new { error = "Not allowed." }, statusCode: 401);

    var emails = new List<string>();
    foreach (var raw in (r.Items ?? Array.Empty<string>()).Take(50))
    {
        var item = (raw ?? "").Trim().ToLowerInvariant();
        if (item.Length == 0) continue;
        if (item.Contains('@') && !item.StartsWith("group:", StringComparison.Ordinal)) { emails.Add(item); continue; }

        var found = await cache.GetOrCreateAsync($"ident:{item}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await Task.Run(() => item.StartsWith("group:", StringComparison.Ordinal)
                ? ad.GroupMemberEmails(item[6..])
                : (ad.FindEmail(item) is { } m ? new List<string> { m } : new List<string>()));
        });
        emails.AddRange(found ?? new List<string>());
    }
    return Results.Ok(new { emails = emails.Distinct().ToList() });
});

app.MapReverseProxy();
app.Run();
record DirItem(string email, string name, string username, string source, string kind);
record ResolveRequest(string[]? Items);