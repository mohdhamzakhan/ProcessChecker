using ProcessChecker.Contracts;

namespace DocumentService;

/// Asks EngineService whether the caller may see (or work on) a request.
public class InstanceAccess
{
    private readonly IHttpClientFactory _factory;
    private readonly IConfiguration _cfg;
    private readonly ILogger<InstanceAccess> _log;

    public InstanceAccess(IHttpClientFactory factory, IConfiguration cfg, ILogger<InstanceAccess> log)
    {
        _factory = factory; _cfg = cfg; _log = log;
    }

    public async Task<bool> AllowedAsync(HttpContext http, Guid instanceId, bool work, CancellationToken ct)
    {
        if (http.IsAdmin()) return true;
        if (http.Caller() is not { } email) return false;

        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"api/instances/{instanceId}/access?level={(work ? "work" : "view")}");
        req.Headers.TryAddWithoutValidation(InternalAuth.KeyHeader, _cfg["Gateway:Key"] ?? "");
        req.Headers.TryAddWithoutValidation(InternalAuth.UserHeader, email);
        req.Headers.TryAddWithoutValidation(InternalAuth.RoleHeader, http.CallerRole() ?? "");
        if (http.CallerName() is { } name) 
            req.Headers.TryAddWithoutValidation(InternalAuth.NameHeader, name);
        var groups = http.Request.Headers[InternalAuth.GroupsHeader].ToString();
        if (groups.Length > 0) req.Headers.TryAddWithoutValidation(InternalAuth.GroupsHeader, groups);

        try
        {
            using var res = await _factory.CreateClient("engine").SendAsync(req, ct);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _log.LogWarning("Access check failed: {Msg}", ex.Message);
            return false;
        }
    }
}