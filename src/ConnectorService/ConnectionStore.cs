using System.Text.Json;
using System.Text.RegularExpressions;
using ConnectorService.Connectors;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ConnectorService;

public class ConnectorRegistry
{
    private readonly Dictionary<string, IConnector> _map;
    public ConnectorRegistry(IEnumerable<IConnector> connectors) =>
        _map = connectors.ToDictionary(c => c.Type);

    public IConnector Get(string type) =>
        _map.TryGetValue(type, out var c)
            ? c
            : throw new InvalidOperationException($"Unsupported connection type '{type}'.");
}

public class ConnectionStore
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    private static readonly Regex NameRegex = new("^[A-Za-z0-9_-]{1,100}$", RegexOptions.Compiled);

    private readonly ConnectorDb _db;
    private readonly IDataProtector _protector;
    private readonly string[] _roots;

    public ConnectionStore(ConnectorDb db, IDataProtectionProvider dp, IConfiguration cfg)
    {
        _db = db;
        _protector = dp.CreateProtector("ProcessChecker.Connections.v1");
        _roots = cfg.GetSection("FileRoots").Get<string[]>() ?? Array.Empty<string>();
    }

    public List<string> Validate(ConnectionRequest r, bool isUpdate)
    {
        var errors = new List<string>();

        if (!NameRegex.IsMatch(r.Name ?? ""))
            errors.Add("Name is required (letters, numbers, '_' and '-').");
        if (!ConnectionTypes.All.Contains(r.Type))
            errors.Add($"Type must be one of: {string.Join(", ", ConnectionTypes.All)}.");
        else if (ConnectionTypes.ServerBased.Contains(r.Type))
        {
            if (!isUpdate && string.IsNullOrWhiteSpace(r.ConnectionString))
                errors.Add("ConnectionString is required for this type.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(r.FilePath))
                errors.Add("FilePath is required for this type.");
            else if (_roots.Length > 0)
            {
                var full = Path.GetFullPath(r.FilePath);
                var allowed = _roots.Any(root =>
                {
                    var rf = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
                    return full.StartsWith(rf, StringComparison.OrdinalIgnoreCase);
                });
                if (!allowed)
                    errors.Add($"FilePath must be inside one of: {string.Join(", ", _roots)}");
            }
        }
        return errors;
    }

    public Task<ConnectionEntity?> GetEntityAsync(string name) =>
        _db.Connections.FirstOrDefaultAsync(x => x.Name == name);

    public ResolvedConnection Resolve(ConnectionEntity e) => new(
        e.Name, e.Type,
        string.IsNullOrEmpty(e.ProtectedConnectionString) ? null : _protector.Unprotect(e.ProtectedConnectionString),
        e.FilePath,
        string.IsNullOrEmpty(e.OptionsJson)
            ? new FileSourceOptions()
            : JsonSerializer.Deserialize<FileSourceOptions>(e.OptionsJson, J) ?? new FileSourceOptions());

    // For testing a connection that is not saved yet
    public ResolvedConnection ResolveRequest(ConnectionRequest r) =>
        new(r.Name, r.Type, r.ConnectionString, r.FilePath, r.Options ?? new FileSourceOptions());

    public ConnectionInfo ToInfo(ConnectionEntity e) => new(
        e.Id, e.Name, e.Type, e.FilePath,
        string.IsNullOrEmpty(e.OptionsJson) ? null : JsonSerializer.Deserialize<FileSourceOptions>(e.OptionsJson, J),
        e.Description, e.IsActive, !string.IsNullOrEmpty(e.ProtectedConnectionString), e.CreatedAt);

    public async Task<ConnectionEntity> CreateAsync(ConnectionRequest r)
    {
        var e = new ConnectionEntity { Name = r.Name };
        Apply(e, r, keepSecretIfEmpty: false);
        _db.Connections.Add(e);
        await _db.SaveChangesAsync();
        return e;
    }

    public async Task<ConnectionEntity?> UpdateAsync(string name, ConnectionRequest r)
    {
        var e = await GetEntityAsync(name);
        if (e is null) return null;
        Apply(e, r, keepSecretIfEmpty: true);   // blank ConnectionString = keep the old one
        await _db.SaveChangesAsync();
        return e;
    }

    public async Task<bool> DeleteAsync(string name)
    {
        var e = await GetEntityAsync(name);
        if (e is null) return false;
        _db.Connections.Remove(e);
        await _db.SaveChangesAsync();
        return true;
    }

    private void Apply(ConnectionEntity e, ConnectionRequest r, bool keepSecretIfEmpty)
    {
        e.Type = r.Type;
        e.FilePath = r.FilePath;
        e.Description = r.Description;
        e.IsActive = r.IsActive ?? true;
        e.OptionsJson = r.Options is null ? null : JsonSerializer.Serialize(r.Options, J);

        if (!string.IsNullOrWhiteSpace(r.ConnectionString))
            e.ProtectedConnectionString = _protector.Protect(r.ConnectionString);
        else if (!keepSecretIfEmpty)
            e.ProtectedConnectionString = null;
    }
}