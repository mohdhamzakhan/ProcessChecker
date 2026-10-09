using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConnectorService.Connectors;

/// Query = JSONPath, e.g.  $.requests[?(@.status=='new')]   (empty = whole file)
public class JsonFileConnector : IConnector
{
    public string Type => ConnectionTypes.Json;

    public async Task<TestResult> TestAsync(ResolvedConnection c, CancellationToken ct)
    {
        try
        {
            var full = FileHelper.RequireFile(c);
            JToken.Parse(await File.ReadAllTextAsync(full, ct));
            return new TestResult(true, $"Valid JSON file ({new FileInfo(full).Length} bytes).");
        }
        catch (Exception ex) { return new TestResult(false, ex.Message); }
    }

    public async Task<QueryResult> QueryAsync(ResolvedConnection c, string? query,
        IDictionary<string, object?>? parameters, int maxRows, CancellationToken ct)
    {
        var full = FileHelper.RequireFile(c);
        var root = JToken.Parse(await File.ReadAllTextAsync(full, ct));
        var q = FileHelper.ApplyParameters(query ?? "", parameters, quote: true);

        IEnumerable<JToken> items;
        if (string.IsNullOrWhiteSpace(q))
            items = root is JArray arr ? arr.Children() : new[] { root };
        else
        {
            var found = root.SelectTokens(q).ToList();
            items = found.Count == 1 && found[0] is JArray a ? a.Children() : found;
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
                        : prop.Value.ToString(Formatting.None);
            else
                row["value"] = item is JValue v ? v.Value : item.ToString(Formatting.None);

            foreach (var k in row.Keys)
                if (!cols.Contains(k, StringComparer.OrdinalIgnoreCase)) cols.Add(k);
            rows.Add(row);
        }
        return new QueryResult(cols, rows, truncated);
    }

    public async Task<List<string>> GetTablesAsync(ResolvedConnection c, CancellationToken ct)
    {
        var full = FileHelper.RequireFile(c);
        var root = JToken.Parse(await File.ReadAllTextAsync(full, ct));
        return root is JObject o
            ? o.Properties().Select(p => "$." + p.Name).ToList()
            : new List<string> { "$ (root array)" };
    }
}