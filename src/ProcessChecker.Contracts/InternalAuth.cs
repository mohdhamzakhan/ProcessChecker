using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ProcessChecker.Contracts;

/// Backend services trust these headers ONLY because the gateway sets them
/// and every service rejects calls that do not carry the shared key.
public static class InternalAuth
{
    public const string KeyHeader = "X-Gateway-Key";
    public const string UserHeader = "X-User-Email";
    public const string RoleHeader = "X-User-Role";

    public static string? Caller(this HttpContext c) =>
        c.Request.Headers[UserHeader].FirstOrDefault()?.Trim().ToLowerInvariant() is { Length: > 0 } s ? s : null;

    public static string? CallerRole(this HttpContext c) =>
        c.Request.Headers[RoleHeader].FirstOrDefault()?.Trim() is { Length: > 0 } s ? s : null;

    public const string NameHeader = "X-User-Name";
    public static string? CallerName(this HttpContext c)
    {
        var v = c.Request.Headers[NameHeader].FirstOrDefault()?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(v)) return null;
        var i = v.IndexOf('\\');
        return i >= 0 ? v[(i + 1)..] : v;
    }

    public static bool IsAdmin(this HttpContext c) => c.CallerRole() == "Admin";

    /// Rejects /api calls that do not carry the shared key. Does nothing when Gateway:Key is not set.
    public static WebApplication UseInternalKey(this WebApplication app)
    {
        var expected = app.Configuration["Gateway:Key"];
        if (string.IsNullOrWhiteSpace(expected))
        {
            app.Logger.LogWarning("Gateway:Key is not set: this service accepts direct, unauthenticated calls.");
            return app;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path;
            var guarded = path.StartsWithSegments("/api")
                          && !path.StartsWithSegments("/api/health")
                          && !HttpMethods.IsOptions(ctx.Request.Method);
            if (guarded)
            {
                var given = Encoding.UTF8.GetBytes(ctx.Request.Headers[KeyHeader].ToString());
                if (given.Length != expectedBytes.Length ||
                    !CryptographicOperations.FixedTimeEquals(given, expectedBytes))
                {
                    ctx.Response.StatusCode = 401;
                    await ctx.Response.WriteAsJsonAsync(new { error = "Call this service through the gateway." });
                    return;
                }
            }
            await next();
        });
        return app;
    }
}