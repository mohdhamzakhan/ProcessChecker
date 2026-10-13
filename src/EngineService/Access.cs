using Microsoft.EntityFrameworkCore;
using ProcessChecker.Contracts;

namespace EngineService;

public static class PartRole
{
    public const string Starter = "Starter";
    public const string Observer = "Observer";
    public const string Assignee = "Assignee";
    public const string Approver = "Approver";
}

/// One row per person per request. This is what decides who may see a request.
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

        foreach (var s in i.Steps)
        {
            foreach (var a in TaskMailer.Split(s.Assignees)) yield return (a, PartRole.Assignee);
            foreach (var a in s.Approvals) yield return (a.Approver, PartRole.Approver);
        }
    }
}

public static class Access
{
    /// The requests the caller may see: admins see all, everyone else only the ones they take part in.
    public static IQueryable<ProcessInstance> Visible(this EngineDb db, HttpContext http)
    {
        if (http.IsAdmin()) return db.Instances;
        var email = http.Caller() ?? "";
        return db.Instances.Where(i => db.Participants.Any(p => p.InstanceId == i.Id && p.Email == email));
    }

    /// May the caller act on the request (approve, submit, upload, retry)? Observers may not.
    public static async Task<bool> CanWorkAsync(this EngineDb db, HttpContext http, Guid id, CancellationToken ct)
    {
        if (http.IsAdmin()) return true;
        var email = http.Caller();
        if (email is null) return false;
        return await db.Participants.AnyAsync(
            p => p.InstanceId == id && p.Email == email && p.Role != PartRole.Observer, ct);
    }
}