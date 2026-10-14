using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace ConnectorService.Connectors;

public class ApiConfig
{
    public static readonly string[] AuthTypes = { "None", "Basic", "Bearer", "ApiKey", "Windows" };
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    private static readonly Regex HeaderRegex = new("^[A-Za-z0-9-]{1,100}$", RegexOptions.Compiled);

    public string AuthType { get; set; } = "None";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Token { get; set; }
    public string? HeaderName { get; set; }
    public string? HeaderValue { get; set; }
    public string? TestPath { get; set; }
    public bool AllowPost { get; set; }
    public bool IgnoreCertErrors { get; set; }
    public int TimeoutSeconds { get; set; } = 30;

    public static (ApiConfig? Config, string? Error) Parse(string? json)
    {
        ApiConfig cfg;
        if (string.IsNullOrWhiteSpace(json)) cfg = new ApiConfig();
        else
        {
            try { cfg = JsonSerializer.Deserialize<ApiConfig>(json, J) ?? new ApiConfig(); }
            catch (JsonException) { return (null, "The API settings are not valid JSON."); }
        }

        if (!AuthTypes.Contains(cfg.AuthType))
            return (null, $"The sign-in method must be one of: {string.Join(", ", AuthTypes)}.");
        if (cfg.AuthType == "Basic" && string.IsNullOrWhiteSpace(cfg.Username))
            return (null, "A user name is required for Basic sign-in.");
        if (cfg.AuthType == "Bearer" && string.IsNullOrWhiteSpace(cfg.Token))
            return (null, "A token is required for Bearer sign-in.");
        if (cfg.AuthType == "ApiKey" && (!HeaderRegex.IsMatch(cfg.HeaderName ?? "") || string.IsNullOrEmpty(cfg.HeaderValue)))
            return (null, "An API key needs a header name (letters, digits, -) and a value.");
        if (cfg.TimeoutSeconds is < 1 or > 120)
            return (null, "The timeout must be between 1 and 120 seconds.");
        if (!string.IsNullOrWhiteSpace(cfg.TestPath) && !cfg.TestPath.StartsWith('/'))
            return (null, "The test address must start with /.");
        return (cfg, null);
    }
}

public class ApiConnector : IConnector
{
    public string Type => ConnectionTypes.Api;

    private const int MaxBytes = 5 * 1024 * 1024;
    private static readonly Regex ParamPlain = new(@"(?<!:):([A-Za-z_]\w*)", RegexOptions.Compiled);
    // group 1 = a whole quoted value (":id"), group 2 = a token inside other text
    private static readonly Regex ParamBody = new("\":([A-Za-z_]\\w*)\"|(?<!:):([A-Za-z_]\\w*)", RegexOptions.Compiled);
    private static readonly Regex FirstLine = new(@"^(GET|POST)\s+(\S+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeaderOk = new("^[A-Za-z0-9-]{1,100}$", RegexOptions.Compiled);
    private static readonly string[] Blocked =
        { "host", "content-length", "content-type", "transfer-encoding", "connection", "authorization", "cookie", "proxy-authorization" };
    private static readonly ConcurrentDictionary<string, HttpClient> Clients = new();

    private record Spec(string Method, string Path, string? ResultPath, List<(string Name, string Value)> Headers, string Body);
    private record Reply(int Status, string Reason, string Text, string? ContentType);

    // ------------------------------------------------------------ interface
    public async Task<TestResult> TestAsync(ResolvedConnection c, CancellationToken ct)
    {
        try
        {
            var (cfg, err) = ApiConfig.Parse(c.ConnectionString);
            if (err != null) return new TestResult(false, err);

            var custom = !string.IsNullOrWhiteSpace(cfg!.TestPath);
            var uri = BuildUri(c, custom ? cfg.TestPath! : "/");
            var reply = await SendAsync(cfg, "GET", uri, new List<(string, string)>(), null, ct);

            if (reply.Status is >= 200 and < 300)
                return new TestResult(true, $"The API answered HTTP {reply.Status}.");
            if (!custom && reply.Status is 404 or 405)
                return new TestResult(true,
                    $"The server is reachable (HTTP {reply.Status} for “/”). Set a test address to also check the sign-in.");

            EnsureOk(reply);
            return new TestResult(false, $"The API answered HTTP {reply.Status}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new TestResult(false, ex.Message);
        }
    }

    public async Task<QueryResult> QueryAsync(ResolvedConnection c, string? query,
        IDictionary<string, object?>? parameters, int maxRows, CancellationToken ct)
    {
        var (cfg, err) = ApiConfig.Parse(c.ConnectionString);
        if (err != null) throw new InvalidOperationException(err);
        var spec = ParseQuery(query, cfg!);

        var path = ParamPlain.Replace(spec.Path, m => Uri.EscapeDataString(Text(Lookup(parameters, m.Groups[1].Value))));
        var uri = BuildUri(c, path);

        var headers = spec.Headers
            .Select(h => (h.Name, Value: ParamPlain.Replace(h.Value, m => Text(Lookup(parameters, m.Groups[1].Value)))
                .Replace("\r", "").Replace("\n", "")))
            .ToList();

        string? body = null;
        if (spec.Body.Length > 0)
            body = ParamBody.Replace(spec.Body, m =>
            {
                if (m.Groups[1].Success) return JsonLiteral(Lookup(parameters, m.Groups[1].Value));
                var text = Text(Lookup(parameters, m.Groups[2].Value));
                return JsonSerializer.Serialize(text)[1..^1];
            });

        var reply = await SendAsync(cfg!, spec.Method, uri, headers, body, ct);
        EnsureOk(reply);
        return ToRows(reply, spec.ResultPath, maxRows);
    }

    public Task<List<string>> GetTablesAsync(ResolvedConnection c, CancellationToken ct) =>
        Task.FromResult(new List<string>());     // an API has no tables: the endpoint is written in the query

    // ------------------------------------------------------------ query text
    private static Spec ParseQuery(string? query, ApiConfig cfg)
    {
        var text = (query ?? "").Replace("\r\n", "\n").Trim();
        if (text.Length == 0)
            throw new InvalidOperationException("The query is empty. Write for example:  GET /employees/:id");

        var lines = text.Split('\n');
        var m = FirstLine.Match(lines[0].Trim());
        if (!m.Success)
            throw new InvalidOperationException("The first line must be  GET /path  or  POST /path");

        var method = m.Groups[1].Value.ToUpperInvariant();
        var path = m.Groups[2].Value;
        if (method == "POST" && !cfg.AllowPost)
            throw new InvalidOperationException("POST is not allowed on this connection. Tick “Allow POST” in its settings.");
        if (!path.StartsWith('/') || path.StartsWith("//") || path.Contains("://") || path.Contains('\\'))
            throw new InvalidOperationException("The path must start with a single / and cannot contain a server name.");

        string? resultPath = null;
        var headers = new List<(string Name, string Value)>();
        var i = 1;
        for (; i < lines.Length; i++)
        {
            var l = lines[i].Trim();
            if (l.Length == 0) { i++; break; }                       // the body starts after the blank line
            if (l.StartsWith("->")) { resultPath = l[2..].Trim(); continue; }

            var colon = l.IndexOf(':');
            if (colon <= 0)
                throw new InvalidOperationException($"Line {i + 1} must be “Name: value”, “-> $.path”, or a blank line before the body.");
            var name = l[..colon].Trim();
            if (!HeaderOk.IsMatch(name))
                throw new InvalidOperationException($"“{name}” is not a valid header name.");
            if (Blocked.Contains(name.ToLowerInvariant()))
                throw new InvalidOperationException($"The header {name} cannot be set in a query.");
            headers.Add((name, l[(colon + 1)..].Trim()));
        }

        var body = i < lines.Length ? string.Join("\n", lines.Skip(i)).Trim() : "";
        if (method == "GET" && body.Length > 0)
            throw new InvalidOperationException("A GET request cannot have a body.");
        if (body.Length > 100_000)
            throw new InvalidOperationException("The body is too large.");

        return new Spec(method, path, string.IsNullOrWhiteSpace(resultPath) ? null : resultPath, headers, body);
    }

    private static object? Lookup(IDictionary<string, object?>? p, string name)
    {
        if (p != null)
            foreach (var kv in p)
                if (string.Equals(kv.Key.TrimStart(':', '@'), name, StringComparison.OrdinalIgnoreCase))
                    return kv.Value ?? "";
        throw new InvalidOperationException($"Missing value for parameter '{name}'.");
    }

    private static string Text(object? v) => v switch
    {
        null => "",
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? ""
    };

    private static string JsonLiteral(object? v) => v switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        string s => JsonSerializer.Serialize(s),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => JsonSerializer.Serialize(v.ToString())
    };

    // ------------------------------------------------------------ http
    private static Uri BuildUri(ResolvedConnection c, string path)
    {
        var baseUrl = (c.FilePath ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var b) ||
            (b.Scheme != Uri.UriSchemeHttp && b.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("The connection has no valid base address.");

        if (!Uri.TryCreate(baseUrl + path, UriKind.Absolute, out var u) ||
            !string.Equals(u.Host, b.Host, StringComparison.OrdinalIgnoreCase) ||
            u.Port != b.Port || u.Scheme != b.Scheme)
            throw new InvalidOperationException("The path leads away from the base address.");
        return u;
    }

    private static HttpClient ClientFor(ApiConfig cfg)
    {
        var windows = cfg.AuthType == "Windows";
        return Clients.GetOrAdd($"{windows}|{cfg.IgnoreCertErrors}", _ =>
        {
            var h = new HttpClientHandler { AllowAutoRedirect = false, UseDefaultCredentials = windows };
            if (cfg.IgnoreCertErrors)
                h.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            return new HttpClient(h) { Timeout = Timeout.InfiniteTimeSpan };
        });
    }

    private static async Task<Reply> SendAsync(ApiConfig cfg, string method, Uri uri,
        List<(string Name, string Value)> headers, string? body, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(cfg.TimeoutSeconds));

        using var req = new HttpRequestMessage(new HttpMethod(method), uri);
        req.Headers.Accept.ParseAdd("application/json");

        switch (cfg.AuthType)
        {
            case "Basic":
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{cfg.Username}:{cfg.Password}")));
                break;
            case "Bearer":
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cfg.Token);
                break;
            case "ApiKey":
                req.Headers.TryAddWithoutValidation(cfg.HeaderName!, cfg.HeaderValue);
                break;
        }
        foreach (var (name, value) in headers) req.Headers.TryAddWithoutValidation(name, value);
        if (!string.IsNullOrEmpty(body)) req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        try
        {
            using var res = await ClientFor(cfg).SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var text = await ReadLimited(res.Content, cts.Token);
            return new Reply((int)res.StatusCode, res.ReasonPhrase ?? "", text, res.Content.Headers.ContentType?.MediaType);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException($"The API did not answer within {cfg.TimeoutSeconds} seconds.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("Could not reach the API: " + ex.Message);
        }
    }

    private static async Task<string> ReadLimited(HttpContent content, CancellationToken ct)
    {
        await using var s = await content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = await s.ReadAsync(buf, ct)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > MaxBytes) throw new InvalidOperationException("The API answer is larger than 5 MB.");
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void EnsureOk(Reply r)
    {
        if (r.Status is >= 300 and < 400)
            throw new InvalidOperationException($"The API redirected the request (HTTP {r.Status}). Put the final address in the base address.");
        if (r.Status is 401 or 403)
            throw new InvalidOperationException($"The API refused access (HTTP {r.Status}). Check the sign-in settings of this connection.");
        if (r.Status is < 200 or >= 300)
            throw new InvalidOperationException($"The API answered HTTP {r.Status} {r.Reason}.");
    }

    // ------------------------------------------------------------ answer -> rows
    private static QueryResult ToRows(Reply reply, string? resultPath, int maxRows)
    {
        JToken root;
        try
        {
            using var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(reply.Text))
            { DateParseHandling = Newtonsoft.Json.DateParseHandling.None };
            root = JToken.Load(reader);
        }
        catch (Newtonsoft.Json.JsonException)
        {
            throw new InvalidOperationException(
                $"The API did not return JSON (content type {reply.ContentType ?? "unknown"}).");
        }

        List<JToken> items;
        try
        {
            if (resultPath is null || resultPath == "$")
                items = root is JArray whole ? whole.Children().ToList() : new List<JToken> { root };
            else
            {
                var found = root.SelectTokens(resultPath).ToList();
                items = found.Count == 1 && found[0] is JArray list ? list.Children().ToList() : found;
            }
        }
        catch (Newtonsoft.Json.JsonException)
        {
            throw new InvalidOperationException($"The result path “{resultPath}” is not valid.");
        }

        var cols = new List<string>();
        var rows = new List<Dictionary<string, object?>>();
        var truncated = false;

        foreach (var item in items)
        {
            if (rows.Count >= maxRows) { truncated = true; break; }
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            if (item is JObject obj)
                foreach (var prop in obj.Properties())
                    row[prop.Name] = prop.Value is JValue jv
                        ? jv.Value
                        : prop.Value.ToString(Newtonsoft.Json.Formatting.None);
            else
                row["value"] = item is JValue v ? v.Value : item.ToString(Newtonsoft.Json.Formatting.None);

            foreach (var k in row.Keys)
                if (!cols.Contains(k, StringComparer.OrdinalIgnoreCase)) cols.Add(k);
            rows.Add(row);
        }
        return new QueryResult(cols, rows, truncated);
    }
}