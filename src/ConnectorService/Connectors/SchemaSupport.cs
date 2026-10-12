using System.Data.Common;

namespace ConnectorService.Connectors;

public record ColumnInfo(string Name, string DataType, bool Nullable);

public interface ISchemaConnector
{
    Task<List<string>> GetSchemasAsync(ResolvedConnection c, CancellationToken ct);
    Task<List<string>> GetSchemaTablesAsync(ResolvedConnection c, string schema, CancellationToken ct);
    Task<List<ColumnInfo>> GetColumnsAsync(ResolvedConnection c, string schema, string table, CancellationToken ct);
}

public abstract class SchemaSqlConnector : SqlConnectorBase, ISchemaConnector
{
    protected abstract string Marker { get; }            // ":" for Oracle, "@" for PostgreSQL
    protected abstract string SchemasSql { get; }
    protected abstract string SchemaTablesSql { get; }   // uses {M}sch
    protected abstract string SchemaColumnsSql { get; }  // uses {M}sch and {M}tbl, returns name, type, nullable

    public async Task<List<string>> GetSchemasAsync(ResolvedConnection c, CancellationToken ct) =>
        (await RunAsync(c, SchemasSql, null, 1, ct)).Select(r => r[0]).ToList();

    public async Task<List<string>> GetSchemaTablesAsync(ResolvedConnection c, string schema, CancellationToken ct) =>
        (await RunAsync(c, SchemaTablesSql, new() { ["sch"] = schema }, 1, ct)).Select(r => r[0]).ToList();

    public async Task<List<ColumnInfo>> GetColumnsAsync(
        ResolvedConnection c, string schema, string table, CancellationToken ct) =>
        (await RunAsync(c, SchemaColumnsSql, new() { ["sch"] = schema, ["tbl"] = table }, 3, ct))
            .Select(r => new ColumnInfo(r[0], r[1], r[2].StartsWith("Y", StringComparison.OrdinalIgnoreCase)))
            .ToList();

    private async Task<List<string[]>> RunAsync(
        ResolvedConnection c, string sql, Dictionary<string, string>? ps, int cols, CancellationToken ct)
    {
        await using var conn = Create(c);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 30;
        Prepare(cmd);
        cmd.CommandText = sql.Replace("{M}", Marker);
        if (ps != null)
            foreach (var (k, v) in ps)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = k;
                p.Value = v;
                cmd.Parameters.Add(p);
            }

        await using var r = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<string[]>();
        while (await r.ReadAsync(ct))
        {
            var row = new string[cols];
            for (var i = 0; i < cols; i++) row[i] = r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
            rows.Add(row);
        }
        return rows;
    }
}