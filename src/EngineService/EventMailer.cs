using System.Text.Json;
using ProcessChecker.Contracts;

namespace EngineService;

/// The emails configured on the process: "when a file is uploaded" and "when the request ends".
public class EventMailer
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    private readonly NotificationClient _mail;
    private readonly IdentityResolver _ids;
    private readonly DisplayTime _time;
    private readonly IConfiguration _cfg;
    private readonly ILogger<EventMailer> _log;

    public EventMailer(NotificationClient mail, IdentityResolver ids, DisplayTime time, IConfiguration cfg, ILogger<EventMailer> log)
    { _mail = mail; _ids = ids; _time = time; _cfg = cfg; _log = log; }

    private string Link(ProcessInstance i) => $"{(_cfg["Web:BaseUrl"] ?? "").TrimEnd('/')}/instances/{i.Id}";
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public Task FileUploadedAsync(ProcessInstance inst, string stepName, IEnumerable<FileRef> files, string? by, CancellationToken ct)
    {
        var names = files.Select(f => f.FileName ?? f.FileId).ToList();
        return SendAsync(inst, "fileUploaded", (data, rule) => ($"New file uploaded: {inst.Title}", new MailTemplate
        {
            Kind = "file",
            Title = inst.Title,
            Intro = "A new file was added to this request.",
            Message = Clean(data.Render(rule.Message)),
            Details = new List<MailDetail>
            {
                new("Process", inst.ProcessName), new("Step", stepName),
                new(names.Count == 1 ? "File" : "Files", string.Join(", ", names)),
                new("Added by", string.IsNullOrWhiteSpace(by) ? "–" : by)
            },
            ButtonLabel = "Open the request",
            ButtonUrl = Link(inst)
        }), ct);
    }

    public Task EndedAsync(ProcessInstance inst, CancellationToken ct)
    {
        var reason = ReasonOf(inst);
        var kind = inst.Status switch { InstanceStatus.Completed => "completed", InstanceStatus.Rejected => "rejected", _ => "cancelled" };
        var intro = inst.Status switch
        {
            InstanceStatus.Completed => "This request has been completed.",
            InstanceStatus.Rejected => "This request was rejected.",
            _ => "This request was cancelled."
        };

        return SendAsync(inst, "requestEnded", (data, rule) =>
        {
            var rows = new List<MailDetail> { new("Process", inst.ProcessName), new("Result", inst.Status) };
            if (!string.IsNullOrWhiteSpace(reason)) rows.Add(new("Reason", reason));
            rows.Add(new("Started", _time.Format(inst.CreatedAt)));
            if (inst.CompletedAt != null) rows.Add(new("Finished", _time.Format(inst.CompletedAt)));

            return ($"{inst.Status}: {inst.Title}", new MailTemplate
            {
                Kind = kind,
                Title = inst.Title,
                Intro = intro,
                Message = Clean(data.Render(rule.Message)),
                Details = rows,
                ButtonLabel = "Open the request",
                ButtonUrl = Link(inst)
            });
        }, ct);
    }

    private async Task SendAsync(ProcessInstance inst, string on,
        Func<DataBag, NotifyRule, (string Subject, MailTemplate Template)> build, CancellationToken ct)
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
                var (subject, template) = build(data, rule);
                await _mail.SendTemplateAsync(emails, subject, template, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { _log.LogWarning("Email rule '{On}' failed for {Id}: {Msg}", on, inst.Id, ex.Message); }
    }

    private static string? ReasonOf(ProcessInstance inst)
    {
        foreach (var s in inst.Steps.OrderByDescending(x => x.OrderNo))
        {
            if (!string.IsNullOrEmpty(s.OutputJson)
                && (s.Status == StepState.Rejected || (s.StepType == StepTypes.End && s.Status == StepState.Completed)))
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