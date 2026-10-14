using System.Globalization;
using ProcessChecker.Contracts;

namespace EngineService;

public static class Conditions
{
    /// Pass = the step should run. Why = the reason it was skipped (shown in the request history).
    public static (bool Pass, string Why) Evaluate(RunIfDefinition? r, DataBag data)
    {
        if (r is null || r.Conditions.Count == 0) return (true, "");

        var any = string.Equals(r.Match, "any", StringComparison.OrdinalIgnoreCase);
        var notes = new List<string>();

        foreach (var c in r.Conditions)
        {
            var (ok, note) = Check(c, data);
            if (any && ok) return (true, "");
            if (!any && !ok) return (false, note);
            notes.Add(note);
        }
        return any ? (false, string.Join("; ", notes)) : (true, "");
    }

    private static (bool, string) Check(ConditionDefinition c, DataBag data)
    {
        var actual = (data.GetString(c.Field.Trim()) ?? "").Trim();
        var expected = data.Render(c.Value).Trim();
        var op = (c.Op ?? "eq").ToLowerInvariant();

        var ok = op switch
        {
            "empty" => actual.Length == 0,
            "notempty" => actual.Length > 0,
            "eq" => Same(actual, expected),
            "ne" => !Same(actual, expected),
            "contains" => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            "notcontains" => !actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            "in" => Items(expected).Any(x => Same(actual, x)),
            "notin" => !Items(expected).Any(x => Same(actual, x)),
            "gt" => actual.Length > 0 && expected.Length > 0 && Compare(actual, expected) > 0,
            "gte" => actual.Length > 0 && expected.Length > 0 && Compare(actual, expected) >= 0,
            "lt" => actual.Length > 0 && expected.Length > 0 && Compare(actual, expected) < 0,
            "lte" => actual.Length > 0 && expected.Length > 0 && Compare(actual, expected) <= 0,
            _ => false
        };

        var needs = op switch
        {
            "empty" => "to be empty",
            "notempty" => "to be filled in",
            "eq" => $"to equal '{expected}'",
            "ne" => $"to differ from '{expected}'",
            "gt" => $"to be greater than '{expected}'",
            "gte" => $"to be at least '{expected}'",
            "lt" => $"to be less than '{expected}'",
            "lte" => $"to be at most '{expected}'",
            "contains" => $"to contain '{expected}'",
            "notcontains" => $"not to contain '{expected}'",
            "in" => $"to be one of '{expected}'",
            "notin" => $"to be none of '{expected}'",
            _ => "to match"
        };
        return (ok, $"{c.Field} is '{actual}', the condition needs it {needs}");
    }

    private static IEnumerable<string> Items(string list) =>
        list.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool Same(string a, string b) =>
        TryNumber(a, out var x) && TryNumber(b, out var y)
            ? x == y
            : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static int Compare(string a, string b)
    {
        if (TryNumber(a, out var x) && TryNumber(b, out var y)) return x.CompareTo(y);
        if (DateTime.TryParse(a, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1) &&
            DateTime.TryParse(b, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2)) return d1.CompareTo(d2);
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNumber(string s, out decimal d) =>
        decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out d);
}