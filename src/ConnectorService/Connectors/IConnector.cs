namespace ConnectorService.Connectors;

public record TestResult(bool Success, string Message);
public record QueryResult(List<string> Columns, List<Dictionary<string, object?>> Rows, bool Truncated);

public interface IConnector
{
    string Type { get; }
    Task<TestResult> TestAsync(ResolvedConnection c, CancellationToken ct);
    Task<QueryResult> QueryAsync(ResolvedConnection c, string? query,
        IDictionary<string, object?>? parameters, int maxRows, CancellationToken ct);
    Task<List<string>> GetTablesAsync(ResolvedConnection c, CancellationToken ct);
}