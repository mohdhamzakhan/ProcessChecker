using System.Text.RegularExpressions;
using ConnectorService.Connectors;
using Microsoft.Extensions.Caching.Memory;

namespace ConnectorService;

public class SchemaResolver
{
    public static readonly Regex NameRegex = new(@"^[A-Za-z][A-Za-z0-9_$#]{0,127}$", RegexOptions.Compiled);
    private readonly IMemoryCache _cache;
    public SchemaResolver(IMemoryCache cache) => _cache = cache;

    public static List<string> Split(string? s) =>
        string.IsNullOrWhiteSpace(s)
            ? new List<string>()
            : s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// The schemas people may use: the admin's allow-list if there is one, otherwise what the DB user can see.
    public async Task<List<string>> AvailableAsync(
        ConnectionEntity e, ResolvedConnection rc, IConnector connector, CancellationToken ct)
    {
        if (connector is not ISchemaConnector sc)
            throw new InvalidOperationException(
                $"Schemas are not supported for {e.Type} connections. Use schema.table in the query.");

        var fixedList = Split(e.AllowedSchemas);
        if (fixedList.Count > 0) return fixedList;

        var key = $"schemas:{e.Id}:{e.ProtectedConnectionString?.GetHashCode()}";
        return (await _cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await sc.GetSchemasAsync(rc, ct);
        }))!;
    }

    /// Returns the exact schema name to use, or null for "the connection's own schema".
    public async Task<string?> ResolveAsync(
        ConnectionEntity e, ResolvedConnection rc, IConnector connector, string? requested, CancellationToken ct)
    {
        var wanted = string.IsNullOrWhiteSpace(requested) ? e.DefaultSchema : requested.Trim();
        if (string.IsNullOrWhiteSpace(wanted)) return null;

        if (!NameRegex.IsMatch(wanted))
            throw new InvalidOperationException($"'{wanted}' is not a valid schema name.");

        var list = await AvailableAsync(e, rc, connector, ct);
        return list.FirstOrDefault(s => s.Equals(wanted, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException(
                   $"Schema '{wanted}' is not available on connection '{e.Name}'.");
    }
}