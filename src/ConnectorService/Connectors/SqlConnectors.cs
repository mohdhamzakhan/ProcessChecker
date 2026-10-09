using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Oracle.ManagedDataAccess.Client;

namespace ConnectorService.Connectors;

public class OracleConnector : SqlConnectorBase
{
    public override string Type => ConnectionTypes.Oracle;
    protected override DbConnection Create(ResolvedConnection c) => new OracleConnection(c.ConnectionString);
    protected override string? TestSql => "SELECT 1 FROM DUAL";
    protected override string TablesSql =>
        "SELECT table_name FROM user_tables UNION SELECT view_name FROM user_views ORDER BY 1";
    protected override void Prepare(DbCommand cmd)
    {
        if (cmd is OracleCommand o) o.BindByName = true;   // :name parameters
    }
}

public class SqlServerConnector : SqlConnectorBase
{
    public override string Type => ConnectionTypes.SqlServer;
    protected override DbConnection Create(ResolvedConnection c) =>
        new Microsoft.Data.SqlClient.SqlConnection(c.ConnectionString);
    protected override string? TestSql => "SELECT 1";
    protected override string TablesSql =>
        "SELECT TABLE_SCHEMA + '.' + TABLE_NAME FROM INFORMATION_SCHEMA.TABLES ORDER BY 1";
}

public class MySqlConnectorImpl : SqlConnectorBase
{
    public override string Type => ConnectionTypes.MySql;
    protected override DbConnection Create(ResolvedConnection c) =>
        new MySqlConnector.MySqlConnection(c.ConnectionString);
    protected override string? TestSql => "SELECT 1";
    protected override string TablesSql =>
        "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() ORDER BY 1";
}

public class PostgreSqlConnector : SqlConnectorBase
{
    public override string Type => ConnectionTypes.PostgreSql;
    protected override DbConnection Create(ResolvedConnection c) =>
        new Npgsql.NpgsqlConnection(c.ConnectionString);
    protected override string? TestSql => "SELECT 1";
    protected override string TablesSql =>
        "SELECT table_schema || '.' || table_name FROM information_schema.tables " +
        "WHERE table_schema NOT IN ('pg_catalog','information_schema') ORDER BY 1";
}

/// Windows only. Needs the "Microsoft Access Database Engine" (ACE) driver installed
/// for the same bitness as the app (64-bit).
public class AccessConnector : SqlConnectorBase
{
    public override string Type => ConnectionTypes.Access;

    protected override DbConnection Create(ResolvedConnection c)
    {
        var cs = !string.IsNullOrWhiteSpace(c.ConnectionString)
            ? c.ConnectionString
            : $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={c.FilePath};Persist Security Info=False;";
        return new System.Data.OleDb.OleDbConnection(cs);
    }

    protected override string? TestSql => null;       // opening the file is the test
    protected override string TablesSql => "";        // not used, see override below

    // OleDb only supports positional "?" so :name / @name are rewritten in order
    protected override string BindParameters(
        DbCommand cmd, string query, IDictionary<string, object?>? p)
    {
        if (p == null || p.Count == 0) return query;
        var lookup = new Dictionary<string, object?>(
            p.ToDictionary(k => k.Key.TrimStart(':', '@'), v => v.Value),
            StringComparer.OrdinalIgnoreCase);

        return Regex.Replace(query, @"[:@]([A-Za-z_]\w*)", m =>
        {
            var name = m.Groups[1].Value;
            if (!lookup.TryGetValue(name, out var v))
                throw new InvalidOperationException($"Missing value for parameter '{name}'.");
            var prm = cmd.CreateParameter();
            prm.Value = v ?? DBNull.Value;
            cmd.Parameters.Add(prm);
            return "?";
        });
    }

    public override async Task<List<string>> GetTablesAsync(ResolvedConnection c, CancellationToken ct)
    {
        await using var conn = Create(c);
        await conn.OpenAsync(ct);
        var dt = conn.GetSchema("Tables");
        return dt.Rows.Cast<DataRow>()
            .Where(r => r["TABLE_TYPE"] is "TABLE" or "VIEW")
            .Select(r => r["TABLE_NAME"].ToString()!)
            .OrderBy(x => x)
            .ToList();
    }
}