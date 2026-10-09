using System.Text.Json;

namespace ConnectorService;

public static class ConnectionTypes
{
    public const string Oracle = "Oracle";
    public const string SqlServer = "SqlServer";
    public const string MySql = "MySql";
    public const string PostgreSql = "PostgreSql";
    public const string Access = "Access";
    public const string Json = "Json";
    public const string Csv = "Csv";

    public static readonly string[] ServerBased = { Oracle, SqlServer, MySql, PostgreSql };
    public static readonly string[] FileBased = { Access, Json, Csv };
    public static readonly string[] All = ServerBased.Concat(FileBased).ToArray();
}

public class FileSourceOptions
{
    public string Delimiter { get; set; } = ",";     // use "\t" or "tab" for tab-separated
    public bool HasHeader { get; set; } = true;
    public string Encoding { get; set; } = "utf-8";
}

public class ConnectionEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string? ProtectedConnectionString { get; set; }   // encrypted
    public string? FilePath { get; set; }
    public string? OptionsJson { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public record ConnectionRequest(
    string Name, string Type, string? ConnectionString, string? FilePath,
    FileSourceOptions? Options, string? Description, bool? IsActive);

public record ConnectionInfo(
    Guid Id, string Name, string Type, string? FilePath, FileSourceOptions? Options,
    string? Description, bool IsActive, bool HasSecret, DateTime CreatedAt);

public record ResolvedConnection(
    string Name, string Type, string? ConnectionString, string? FilePath, FileSourceOptions Options);

public record QueryRequest(string? Query, Dictionary<string, JsonElement>? Parameters, int? MaxRows);

public record TypeDescriptor(string Type, string Label, string Kind, string Example, string QueryHelp);