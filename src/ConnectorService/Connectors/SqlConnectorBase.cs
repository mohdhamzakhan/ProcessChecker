using System.Data.Common;

namespace ConnectorService.Connectors;

public abstract class SqlConnectorBase : IConnector
{
    public abstract string Type { get; }
    protected abstract DbConnection Create(ResolvedConnection c);
    protected abstract string? TestSql { get; }
    protected abstract string TablesSql { get; }
    protected virtual void Prepare(DbCommand cmd) { }

    protected virtual string BindParameters(
        DbCommand cmd, string query, IDictionary<string, object?>? p)
    {
        if (p == null) return query;
        foreach (var (key, value) in p)
        {
            var prm = cmd.CreateParameter();
            prm.ParameterName = key.TrimStart(':', '@');
            prm.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(prm);
        }
        return query;
    }

    public async Task<TestResult> TestAsync(ResolvedConnection c, CancellationToken ct)
    {
        try
        {
            await using var conn = Create(c);
            await conn.OpenAsync(ct);
            if (TestSql != null)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = TestSql;
                cmd.CommandTimeout = 15;
                await cmd.ExecuteScalarAsync(ct);
            }
            return new TestResult(true, $"Connected (server version {conn.ServerVersion}).");
        }
        catch (Exception ex)
        {
            return new TestResult(false, ex.Message);
        }
    }

    public async Task<QueryResult> QueryAsync(ResolvedConnection c, string? query,
        IDictionary<string, object?>? parameters, int maxRows, CancellationToken ct)
    {
        var safe = SqlGuard.Validate(query);

        await using var conn = Create(c);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 30;
        Prepare(cmd);
        cmd.CommandText = BindParameters(cmd, safe, parameters);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var cols = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        var rows = new List<Dictionary<string, object?>>();
        var truncated = false;

        while (await reader.ReadAsync(ct))
        {
            if (rows.Count >= maxRows) { truncated = true; break; }
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < cols.Count; i++)
                row[cols[i]] = Normalize(reader.GetValue(i));
            rows.Add(row);
        }
        return new QueryResult(cols, rows, truncated);
    }

    public virtual async Task<List<string>> GetTablesAsync(ResolvedConnection c, CancellationToken ct)
    {
        await using var conn = Create(c);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = TablesSql;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<string>();
        while (await reader.ReadAsync(ct))
            list.Add(reader.GetValue(0).ToString() ?? "");
        return list;
    }

    private static object? Normalize(object value) => value switch
    {
        DBNull => null,
        byte[] b => Convert.ToBase64String(b),
        _ => value
    };
}