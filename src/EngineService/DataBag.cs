using System.Text.Json;
using System.Text.RegularExpressions;

namespace EngineService;

/// The data collected by a process. Keys are case-insensitive.
/// Fields are stored twice: "date" (latest wins) and "stepKey.date" (exact).
public class DataBag
{
    private static readonly Regex Placeholder =
        new(@"\{\{\s*([\w.\-]+)\s*\}\}", RegexOptions.Compiled);

    private readonly Dictionary<string, JsonElement> _d = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, JsonElement> All => _d;

    public static DataBag FromJson(string? json)
    {
        var bag = new DataBag();
        if (string.IsNullOrWhiteSpace(json)) return bag;
        var d = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        if (d != null)
            foreach (var kv in d) bag._d[kv.Key] = kv.Value.Clone();
        return bag;
    }

    public static DataBag FromDictionary(IDictionary<string, JsonElement>? d)
    {
        var bag = new DataBag();
        if (d != null)
            foreach (var kv in d) bag._d[kv.Key] = kv.Value.Clone();
        return bag;
    }

    public void Set(string key, object? value) => _d[key] = JsonSerializer.SerializeToElement(value);
    public void SetElement(string key, JsonElement value) => _d[key] = value.Clone();

    public JsonElement? Get(string key) => _d.TryGetValue(key, out var v) ? v : null;
    public string? GetString(string key) => _d.TryGetValue(key, out var v) ? Text(v) : null;

    public string ToJson() => JsonSerializer.Serialize(_d);

    /// Replaces {{name}} placeholders. Unknown names become empty text.
    public string Render(string? template) =>
        string.IsNullOrEmpty(template)
            ? ""
            : Placeholder.Replace(template, m => GetString(m.Groups[1].Value) ?? "");

    public static string Text(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? "",
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => e.GetRawText()
    };
}