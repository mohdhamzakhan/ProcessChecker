using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EngineService;

/// The data collected by a process. Keys are case-insensitive.
/// Fields are stored twice: "date" (latest wins) and "stepKey.date" (exact).
public class DataBag
{
    private static readonly Regex Placeholder =
        new(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.Compiled);
    private static readonly Regex Arith =
        new(@"^([\w.\-]+?)\s*([+-])\s*(\d+)$", RegexOptions.Compiled);

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

    public string? Resolve(string token)
    {
        token = token.Trim();
        var direct = GetString(token);              // a real field called "a-b" wins over "a minus b"
        if (direct != null) return direct;

        var m = Arith.Match(token);
        if (!m.Success) return null;
        var baseValue = GetString(m.Groups[1].Value);
        if (baseValue == null ||
            !decimal.TryParse(baseValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var n))
            return null;

        var delta = decimal.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        var result = m.Groups[2].Value == "+" ? n + delta : n - delta;
        return result.ToString("0.############", CultureInfo.InvariantCulture);
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
         : Placeholder.Replace(template, m => Resolve(m.Groups[1].Value) ?? "");
    public string RenderUrl(string? template, List<string> missing) =>
    string.IsNullOrEmpty(template)
        ? ""
        : Placeholder.Replace(template, m =>
        {
            var token = m.Groups[1].Value.Trim();
            var v = Resolve(token);
            if (string.IsNullOrEmpty(v))
            {
                if (!missing.Contains(token)) missing.Add(token);
                return "";
            }
            return Uri.EscapeDataString(v);
        });
    public static string Text(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? "",
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => e.GetRawText()
    };


}