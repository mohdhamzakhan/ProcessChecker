using Microsoft.EntityFrameworkCore;
using ProcessChecker.Contracts;

namespace EngineService;

public static class PartRole
{
    public const string Starter = "Starter";
    public const string Observer = "Observer";
    public const string Assignee = "Assignee";
    public const string Approver = "Approver";
    public const string Requester = "Requester";
}

/// One row per person per request. "Email" holds the person's identity: an email or a username.
public class Participant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstanceId { get; set; }
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
}

public static class ParticipantRules
{
    public static IEnumerable<(string Email, string Role)> Wanted(ProcessInstance i)
    {
        if (!string.IsNullOrWhiteSpace(i.StartedBy) && i.StartedBy.Contains('@'))
            yield return (i.StartedBy.Trim().ToLowerInvariant(), PartRole.Starter);

        foreach (var o in TaskMailer.Split(i.Observers)) yield return (o, PartRole.Observer);
        foreach (var r in TaskMailer.Split(i.Requester)) yield return (r, PartRole.Requester);

        foreach (var s in i.Steps)
        {
            foreach (var a in TaskMailer.Split(s.Assignees)) yield return (a, PartRole.Assignee);
            foreach (var a in s.Approvals) yield return (a.Approver, PartRole.Approver);
        }
    }
}

public static class Requesters
{
    /// "CORP\JSmith " -> "jsmith". Anything with spaces (a display name) is not an identity.
    public static string? Normalize(string? raw)
    {
        var v = (raw ?? "").Trim().ToLowerInvariant();
        var i = v.IndexOf('\\');
        if (i >= 0) v = v[(i + 1)..];
        return v.Length == 0 || v.Length > 200 || v.Any(char.IsWhiteSpace) ? null : v;
    }

    /// Works out the requester(s) from the process settings and the request data. null = not known yet.
    public static string? Resolve(ProcessDefinitionDto def, DataBag data)
    {
        var list = new List<string>();
        foreach (var t in def.Requesters)
            foreach (var part in data.Render(t).Split(new[] { ';', ',' },
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var id = Normalize(part);
                if (id != null && !list.Contains(id) && list.Count < 10) list.Add(id);
            }
        return TaskMailer.Join(list);
    }
}

public static class Access
{
    /// The caller's identities: the email, the AD username, and `group:name` for each AD group.
    public static List<string> Identities(this HttpContext c)
    {
        var list = new List<string>();
        if (c.Caller() is { } e) list.Add(e);
        if (c.CallerName() is { } n && !list.Contains(n)) list.Add(n);
        foreach (var g in c.CallerGroups()) { var t = "group:" + g; if (!list.Contains(t)) list.Add(t); }
        return list;
    }

    /// The requests the caller may see: admins see all, everyone else only the ones they take part in.
    public static IQueryable<ProcessInstance> Visible(this EngineDb db, HttpContext http)
    {
        if (http.IsAdmin()) return db.Instances;
        var ids = http.Identities();
        return db.Instances.Where(i => db.Participants.Any(p => p.InstanceId == i.Id && ids.Contains(p.Email)));
    }

    /// May the caller act on the request? Observers and requesters may only look.
    public static async Task<bool> CanWorkAsync(this EngineDb db, HttpContext http, Guid id, CancellationToken ct)
    {
        if (http.IsAdmin()) return true;
        var ids = http.Identities();
        if (ids.Count == 0) return false;
        return await db.Participants.AnyAsync(
            p => p.InstanceId == id && ids.Contains(p.Email)
                 && p.Role != PartRole.Observer && p.Role != PartRole.Requester, ct);
    }


}