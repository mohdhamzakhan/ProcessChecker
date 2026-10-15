using System.Net;
using System.Text.Json;
using ProcessChecker.Contracts;

namespace EngineService;

/// The emails configured on the process: "when a file is uploaded" and "when the request ends".
public class EventMailer
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    private readonly NotificationClient _mail;
    private readonly IdentityResolver _ids;
    private readonly IConfiguration _cfg;
    private readonly ILogger<EventMailer> _log;

    public EventMailer(NotificationClient mail, IdentityResolver ids, IConfiguration cfg, ILogger<EventMailer> log)
    { _mail = mail; _ids = ids; _cfg = cfg; _log = log; }

    private static string Enc(string? s) => WebWebUtility(s);
    private static string WebWebUtility(string? s) => WebUtility.HtmlEncode(s ?? "");
    private string Link(ProcessInstance i) => $"{_cfg["Web:BaseUrl"]?.TrimEnd('/')}/instances/{i.Id}";
    private static string Note(string? message) => string.IsNullOrWhiteSpace(message) ? "" : $"<p>{Enc(message)}</p>";

    public Task FileUploadedAsync(ProcessInstance inst, string stepName, IEnumerable<FileRef> files, string? by, CancellationToken ct)
    {
        var names = files.Select(f => f.FileName ?? f.FileId).ToList();
        return SendAsync(inst, "fileUploaded", (data, rule) => (
            $"New file uploaded: {inst.Title}",
            Note(data.Render(rule.Message)) +
            $"<p><b>{Enc(inst.Title)}</b><br/>Step: {Enc(stepName)}<br/>Files: {Enc(string.Join(", ", names))}" +
            (string.IsNullOrWhiteSpace(by) ? "" : $"<br/>By: {Enc(by)}") + "</p>" +
            $"<p><a href=\"{Link(inst)}\">Open the request</a></p>"), ct);
    }

    public Task EndedAsync(ProcessInstance inst, CancellationToken ct)
    {
        var reason = ReasonOf(inst);
        return SendAsync(inst, "requestEnded", (data, rule) => (
            $"{inst.Status}: {inst.Title}",
            Note(data.Render(rule.Message)) +
            $"<p><b>{Enc(inst.Title)}</b><br/>Result: <b>{Enc(inst.Status)}</b>" +
            (string.IsNullOrWhiteSpace(reason) ? "" : $"<br/>{Enc(reason)}") + "</p>" +
            $"<p><a href=\"{Link(inst)}\">Open the request</a></p>"), ct);
    }

    private async Task SendAsync(ProcessInstance inst, string on,
        Func<DataBag, NotifyRule, (string Subject, string Body)> build, CancellationToken ct)
    {
        try
        {
            var def = JsonSerializer.Deserialize<ProcessDefinitionDto>(inst.DefinitionJson, J);
            var rules = def?.Notifications.Where(r => r.On == on).ToList();
            if (rules is null || rules.Count == 0) return;
            var data = DataBag.FromJson(inst.DataJson);

            foreach (var rule in rules)
            {
                var who = new List<string>();
                foreach (var t in rule.To)
                    who.AddRange(data.Render(t).Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                if (rule.ToRequester) who.AddRange(TaskMailer.Split(inst.Requester));
                if (rule.ToStarter && !string.IsNullOrWhiteSpace(inst.StartedBy)) who.Add(inst.StartedBy);

                var emails = await _ids.ToEmailsAsync(who, ct);
                if (emails.Count == 0)
                {
                    _log.LogInformation("Email rule '{On}' for {Id}: no address could be found.", on, inst.Id);
                    continue;
                }
                var (subject, body) = build(data, rule);
                await _mail.SendEmailAsync(emails, subject, body, true, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { _log.LogWarning("Email rule '{On}' failed for {Id}: {Msg}", on, inst.Id, ex.Message); }
    }

    private static string? ReasonOf(ProcessInstance inst)
    {
        foreach (var s in inst.Steps.OrderByDescending(x => x.OrderNo))
        {
            if (s.StepType == StepTypes.End && s.Status == StepState.Completed && !string.IsNullOrEmpty(s.OutputJson))
            {
                try
                {
                    using var d = JsonDocument.Parse(s.OutputJson);
                    if (d.RootElement.TryGetProperty("reason", out var r) && r.GetString() is { Length: > 0 } text) return text;
                }
                catch (JsonException) { }
            }
            if (s.Status == StepState.Rejected)
            {
                var a = s.Approvals.FirstOrDefault(x => x.Decision == "Rejected");
                if (a != null) return $"Rejected by {a.DecidedBy ?? a.Approver}" + (string.IsNullOrWhiteSpace(a.Note) ? "." : $": {a.Note}");
            }
        }
        return null;
    }
}