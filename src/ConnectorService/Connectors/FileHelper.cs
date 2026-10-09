using System.Globalization;
using System.Text.RegularExpressions;

namespace ConnectorService.Connectors;

public static class FileHelper
{
    public static string RequireFile(ResolvedConnection c)
    {
        if (string.IsNullOrWhiteSpace(c.FilePath))
            throw new InvalidOperationException("File path is not configured.");
        var full = Path.GetFullPath(c.FilePath);
        if (!File.Exists(full))
            throw new FileNotFoundException($"File not found: {full}");
        return full;
    }

    /// Replaces :name placeholders with values. quote=true wraps strings (for JSONPath).
    public static string ApplyParameters(string query, IDictionary<string, object?>? p, bool quote)
    {
        if (p == null) return query;
        foreach (var (key, value) in p)
        {
            var name = key.TrimStart(':', '@');
            var literal = value switch
            {
                null => "null",
                bool b => b ? "true" : "false",
                string s => quote ? $"'{s.Replace("\\", "\\\\").Replace("'", "\\'")}'" : s,
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? ""
            };
            query = Regex.Replace(query, $@":{Regex.Escape(name)}\b", _ => literal, RegexOptions.IgnoreCase);
        }
        return query;
    }
}