using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;

namespace ConnectorService.Connectors;

/// Query = filter, e.g.  status = new AND amount > 1000   (empty = all rows)
/// Operators: =  !=  <>  >  <  >=  <=  ~ (contains)
public class CsvFileConnector : IConnector
{
    public string Type => ConnectionTypes.Csv;

    private record Cond(string Col, string Op, string Val);

    public async Task<TestResult> TestAsync(ResolvedConnection c, CancellationToken ct)
    {
        try
        {
            var r = await QueryAsync(c, null, null, 1, ct);
            return new TestResult(true, $"File readable. Columns: {string.Join(", ", r.Columns)}");
        }
        catch (Exception ex) { return new TestResult(false, ex.Message); }
    }

    public async Task<QueryResult> QueryAsync(ResolvedConnection c, string? query,
        IDictionary<string, object?>? parameters, int maxRows, CancellationToken ct)
    {
        var full = FileHelper.RequireFile(c);
        var opt = c.Options;
        var delimiter = opt.Delimiter is "\\t" or "tab" or "\t" ? "\t" : opt.Delimiter;
        var conds = ParseFilter(FileHelper.ApplyParameters(query ?? "", parameters, quote: false));

        using var reader = new StreamReader(full, Encoding.GetEncoding(opt.Encoding));
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HasHeaderRecord = false,
            BadDataFound = null,
            MissingFieldFound = null
        };
        using var csv = new CsvReader(reader, config);

        string[]? headers = null;
        var rows = new List<Dictionary<string, object?>>();
        var truncated = false;

        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            var rec = csv.Parser.Record!;

            if (headers == null)
            {
                if (opt.HasHeader) { headers = rec.Select(h => h.Trim()).ToArray(); continue; }
                headers = Enumerable.Range(1, rec.Length).Select(i => $"Col{i}").ToArray();
            }

            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length; i++)
                row[headers[i]] = i < rec.Length ? rec[i] : null;

            if (!Matches(row, conds)) continue;
            if (rows.Count >= maxRows) { truncated = true; break; }
            rows.Add(row);
        }
        return new QueryResult(headers?.ToList() ?? new List<string>(), rows, truncated);
    }

    public Task<List<string>> GetTablesAsync(ResolvedConnection c, CancellationToken ct) =>
        Task.FromResult(new List<string> { Path.GetFileName(FileHelper.RequireFile(c)) });

    private static List<Cond> ParseFilter(string q)
    {
        var list = new List<Cond>();
        if (string.IsNullOrWhiteSpace(q)) return list;

        foreach (var part in Regex.Split(q, @"\s+AND\s+", RegexOptions.IgnoreCase))
        {
            var m = Regex.Match(part, @"^\s*(\w+)\s*(!=|<>|>=|<=|=|>|<|~)\s*(.*?)\s*$");
            if (!m.Success)
                throw new InvalidOperationException(
                    $"Invalid filter '{part}'. Use: column = value AND other != value");
            list.Add(new Cond(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value.Trim('\'', '"')));
        }
        return list;
    }

    private static bool Matches(Dictionary<string, object?> row, List<Cond> conds)
    {
        foreach (var c in conds)
        {
            if (!row.TryGetValue(c.Col, out var raw))
                throw new InvalidOperationException($"Column '{c.Col}' not found in file.");
            var cell = raw?.ToString() ?? "";
            var cmp = Compare(cell, c.Val);

            var ok = c.Op switch
            {
                "~" => cell.Contains(c.Val, StringComparison.OrdinalIgnoreCase),
                "=" => cmp == 0,
                "!=" or "<>" => cmp != 0,
                ">" => cmp > 0,
                "<" => cmp < 0,
                ">=" => cmp >= 0,
                "<=" => cmp <= 0,
                _ => false
            };
            if (!ok) return false;
        }
        return true;
    }

    private static int Compare(string a, string b) =>
        double.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out var x) &&
        double.TryParse(b, NumberStyles.Any, CultureInfo.InvariantCulture, out var y)
            ? x.CompareTo(y)
            : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
}