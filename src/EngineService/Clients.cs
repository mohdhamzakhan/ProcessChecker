using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EngineService;

public record QueryOutput(List<string> Columns, List<Dictionary<string, JsonElement>> Rows, bool Truncated);

public class DefinitionClient
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    public DefinitionClient(HttpClient http) => _http = http;

    public async Task<DefinitionInfo?> GetLatestAsync(string key, CancellationToken ct)
    {
        try
        {
            var res = await _http.GetAsync($"api/definitions/{Uri.EscapeDataString(key)}", ct);
            if (res.StatusCode == HttpStatusCode.NotFound) return null;
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadFromJsonAsync<DefinitionInfo>(J, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new EngineException(502, $"DefinitionService is unreachable: {ex.Message}");
        }
    }

    public async Task<List<DefinitionInfo>> ListAsync(CancellationToken ct) =>
        await _http.GetFromJsonAsync<List<DefinitionInfo>>("api/definitions", J, ct) ?? new();
}

public class ConnectorClient
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    public ConnectorClient(HttpClient http) => _http = http;

    public async Task<QueryOutput> QueryAsync(
        string connection, string query, Dictionary<string, JsonElement>? parameters,
        int maxRows, CancellationToken ct)
    {
        HttpResponseMessage res;
        try
        {
            res = await _http.PostAsJsonAsync(
                $"api/connections/{Uri.EscapeDataString(connection)}/query",
                new { query, parameters, maxRows }, J, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"ConnectorService is unreachable: {ex.Message}");
        }

        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadError(res, connection));

        return (await res.Content.ReadFromJsonAsync<QueryOutput>(J, ct))!;
    }

    private static async Task<string> ReadError(HttpResponseMessage res, string name)
    {
        if (res.StatusCode == HttpStatusCode.NotFound)
            return $"Connection '{name}' was not found in ConnectorService.";

        var text = await res.Content.ReadAsStringAsync();
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("error", out var e))
                return e.GetString() ?? text;
        }
        catch { /* not json */ }
        return $"ConnectorService error {(int)res.StatusCode}: {text}";
    }
}

public class NotificationClient
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    public NotificationClient(HttpClient http) => _http = http;

    public async Task SendEmailAsync(
        IEnumerable<string> to, string subject, string body, bool isHtml, CancellationToken ct)
    {
        try
        {
            var res = await _http.PostAsJsonAsync("api/notifications/email",
                new { to = to.ToArray(), subject, body, isHtml }, J, ct);
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"NotificationService returned {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync(ct)}");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"NotificationService is unreachable: {ex.Message}");
        }
    }
}