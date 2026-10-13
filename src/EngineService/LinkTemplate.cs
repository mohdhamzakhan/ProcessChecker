using System.Numerics;
using System.Text.RegularExpressions;

namespace EngineService;

public static class LinkTemplate
{
    private static readonly Regex Place = new(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.Compiled);
    private static readonly Regex Arith = new(@"^([\w.\-]+?)\s*([+-])\s*(\d+)$", RegexOptions.Compiled);
    private static readonly Regex Start = new(@"^https?://[^/?#{}\s]+(?=[/?#]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool HostIsFixed(string? template) => Start.IsMatch((template ?? "").Trim());

    public static (string Url, List<string> Missing) Build(string? template, DataBag data)
    {
        var missing = new List<string>();
        var url = Place.Replace((template ?? "").Trim(), m =>
        {
            var token = m.Groups[1].Value.Trim();
            var v = Resolve(data, token);
            if (string.IsNullOrEmpty(v))
            {
                if (!missing.Contains(token)) missing.Add(token);
                return "";
            }
            return Uri.EscapeDataString(v);
        });
        return (url, missing);
    }

    private static string? Resolve(DataBag data, string token)
    {
        var direct = data.GetString(token);
        if (direct != null) return direct;

        var m = Arith.Match(token);
        if (!m.Success) return null;
        var b = data.GetString(m.Groups[1].Value);
        if (b == null || !BigInteger.TryParse(b.Trim(), out var n)) return null;
        var d = BigInteger.Parse(m.Groups[3].Value);
        return (m.Groups[2].Value == "+" ? n + d : n - d).ToString();
    }
}