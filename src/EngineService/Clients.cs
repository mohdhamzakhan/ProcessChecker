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
        int maxRows, string? schema, CancellationToken ct)
    {
        HttpResponseMessage res;
        try
        {
            res = await _http.PostAsJsonAsync(
                $"api/connections/{Uri.EscapeDataString(connection)}/query",
                new { query, parameters, maxRows, schema }, J, ct);
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

public record MailFile(string FileName, string ContentType, byte[] Bytes);

public record MailDetail(string Label, string? Value);
public record MailItem(string Title, string? Sub = null, string? Meta = null, string? Url = null, string? Badge = null);

public class MailTemplate
{
    public string Kind { get; set; } = "info";
    public string? Label { get; set; }
    public string? Title { get; set; }
    public string? Intro { get; set; }
    public string? Message { get; set; }
    public List<MailDetail>? Details { get; set; }
    public List<MailItem>? Items { get; set; }
    public string? ButtonLabel { get; set; }
    public string? ButtonUrl { get; set; }
    public string? Note { get; set; }
}

public class NotificationClient
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    public NotificationClient(HttpClient http) => _http = http;

    /// A designed email: the notification service builds the layout from the template.
    public Task SendTemplateAsync(IEnumerable<string> to, string subject, MailTemplate template,
        CancellationToken ct, IReadOnlyList<MailFile>? files = null) =>
        PostAsync(to, new Dictionary<string, object?> { ["subject"] = subject, ["template"] = template }, files, ct);

    /// A plain message (the notification service still puts it into the design).
    public Task SendEmailAsync(IEnumerable<string> to, string subject, string body, bool isHtml,
        CancellationToken ct, IReadOnlyList<MailFile>? files = null) =>
        PostAsync(to, new Dictionary<string, object?> { ["subject"] = subject, ["body"] = body, ["isHtml"] = isHtml }, files, ct);

    private async Task PostAsync(IEnumerable<string> to, Dictionary<string, object?> content,
        IReadOnlyList<MailFile>? files, CancellationToken ct)
    {
        var all = to.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (all.Count == 0) return;

        var attachments = files?.Select(f => new { fileName = f.FileName, contentType = f.ContentType, base64 = Convert.ToBase64String(f.Bytes) }).ToArray();
        foreach (var chunk in all.Chunk(40))
        {
            var payload = new Dictionary<string, object?>(content) { ["to"] = chunk, ["attachments"] = attachments };
            try
            {
                var res = await _http.PostAsJsonAsync("api/notifications/email", payload, J, ct);
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
}

public class DocumentClient
{
    private readonly HttpClient _http;
    public DocumentClient(HttpClient http) => _http = http;

    public async Task<(byte[] Bytes, string ContentType)> GetAsync(Guid id, long maxBytes, CancellationToken ct)
    {
        using var res = await _http.GetAsync($"api/documents/{id}/download", HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"DocumentService answered {(int)res.StatusCode} for file {id}.");
        if (res.Content.Headers.ContentLength > maxBytes) throw new InvalidOperationException("A file is larger than the attachment limit.");
        var bytes = await res.Content.ReadAsByteArrayAsync(ct);
        if (bytes.LongLength > maxBytes) throw new InvalidOperationException("A file is larger than the attachment limit.");
        return (bytes, res.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
    }
}

/// Turns emails, usernames and group:name into email addresses (usernames and groups are looked up by the gateway).
public class IdentityResolver
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    private record Answer(List<string>? Emails);
    private readonly HttpClient _http;
    private readonly ILogger<IdentityResolver> _log;
    public IdentityResolver(HttpClient http, ILogger<IdentityResolver> log) { _http = http; _log = log; }

    public async Task<List<string>> ToEmailsAsync(IEnumerable<string?> identities, CancellationToken ct)
    {
        var emails = new List<string>();
        var ask = new List<string>();
        foreach (var raw in identities)
        {
            var id = (raw ?? "").Trim().ToLowerInvariant();
            if (id.Length == 0 || id == "system:poll") continue;
            if (id.Contains('@') && !id.StartsWith("group:", StringComparison.Ordinal)) emails.Add(id);
            else ask.Add(id);
        }

        if (ask.Count > 0)
        {
            try
            {
                var res = await _http.PostAsJsonAsync("internal/identities/resolve", new { items = ask.Distinct().ToArray() }, J, ct);
                if (res.IsSuccessStatusCode)
                    emails.AddRange((await res.Content.ReadFromJsonAsync<Answer>(J, ct))?.Emails ?? new List<string>());
                else _log.LogWarning("Resolving people failed: HTTP {Code}", (int)res.StatusCode);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            { _log.LogWarning("Resolving people failed: {Msg}", ex.Message); }
        }
        return emails.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}