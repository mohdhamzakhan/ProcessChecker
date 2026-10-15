using Microsoft.EntityFrameworkCore;

namespace EngineService;

/// While someone is out of office, a new task for them goes to their colleague.
public class AwayService
{
    private readonly EngineDb _db;
    public AwayService(EngineDb db) => _db = db;

    public async Task<(List<string> Result, List<string> Notes)> ApplyAsync(List<string> identities, CancellationToken ct)
    {
        var emails = identities.Where(x => !x.StartsWith("group:")).ToList();
        if (emails.Count == 0) return (identities, new List<string>());

        var now = DateTime.UtcNow;
        var rows = await _db.Away.AsNoTracking()
            .Where(a => a.Active && a.FromAt <= now && a.ToAt >= now && emails.Contains(a.Email))
            .ToListAsync(ct);
        if (rows.Count == 0) return (identities, new List<string>());

        var map = rows.ToDictionary(r => r.Email, r => r.DelegateTo);
        var result = new List<string>();
        var notes = new List<string>();
        foreach (var id in identities)
        {
            if (map.TryGetValue(id, out var to))
            {
                notes.Add($"{id} is out of office, so {to} was used");
                if (!result.Contains(to)) result.Add(to);
            }
            else if (!result.Contains(id)) result.Add(id);
        }
        return (result, notes);
    }
}