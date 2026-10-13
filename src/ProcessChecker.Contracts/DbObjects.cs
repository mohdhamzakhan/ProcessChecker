using System.Data.Common;

namespace ProcessChecker.Contracts;

/// Asks Oracle whether a table, column or index already exists, so startup never has to fail to find out.
public static class DbObjects
{
    public static string Schema =>
        (Environment.GetEnvironmentVariable("ORACLE_SCHEMA") ?? "PROCESSCHECKER").ToUpperInvariant();

    public static Task<bool> TableExistsAsync(DbConnection c, string table) =>
        CountAsync(c, "SELECT COUNT(*) FROM all_tables WHERE owner = :p0 AND table_name = :p1", Schema, table);

    public static Task<bool> ColumnExistsAsync(DbConnection c, string table, string column) =>
        CountAsync(c, "SELECT COUNT(*) FROM all_tab_columns WHERE owner = :p0 AND table_name = :p1 AND column_name = :p2",
            Schema, table, column);

    public static Task<bool> IndexExistsAsync(DbConnection c, string index) =>
        CountAsync(c, "SELECT COUNT(*) FROM all_indexes WHERE owner = :p0 AND index_name = :p1", Schema, index);

    private static async Task<bool> CountAsync(DbConnection c, string sql, params string[] values)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        for (var i = 0; i < values.Length; i++)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = "p" + i;
            p.Value = values[i].ToUpperInvariant();
            cmd.Parameters.Add(p);
        }
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }
}