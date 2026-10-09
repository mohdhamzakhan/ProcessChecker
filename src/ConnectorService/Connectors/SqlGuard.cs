using System.Text.RegularExpressions;

namespace ConnectorService.Connectors;

/// Safety net only. Real protection = connect with a READ-ONLY database user.
public static class SqlGuard
{
    private static readonly Regex Literals = new(@"'(?:[^']|'')*'", RegexOptions.Compiled);

    private static readonly Regex Forbidden = new(
        @"\b(insert|update|delete|drop|alter|truncate|merge|exec|execute|grant|revoke|create|call|begin|declare|into|commit|rollback)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Validate(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new InvalidOperationException("Query is required.");

        var q = query.Trim();
        var stripped = Literals.Replace(q, "''");   // ignore text inside quotes

        if (stripped.Contains(';'))
            throw new InvalidOperationException("Semicolons are not allowed.");
        if (stripped.Contains("--") || stripped.Contains("/*"))
            throw new InvalidOperationException("Comments are not allowed.");
        if (!Regex.IsMatch(stripped, @"^\s*(select|with)\b", RegexOptions.IgnoreCase))
            throw new InvalidOperationException("Only SELECT queries are allowed.");

        var m = Forbidden.Match(stripped);
        if (m.Success)
            throw new InvalidOperationException($"Keyword '{m.Value}' is not allowed.");

        return q;
    }
}