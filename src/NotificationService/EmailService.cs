using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace NotificationService;

public class EmailService
{
    private readonly NotifyDb _db;
    private readonly SmtpOptions _o;
    private readonly ILogger<EmailService> _log;

    public EmailService(NotifyDb db, SmtpOptions options, ILogger<EmailService> log)
    {
        _db = db; _o = options; _log = log;
    }

    // ---------------------------------------------------------------- send a new email
    public async Task<NotificationLog> SendAsync(EmailRequest r, CancellationToken ct)
    {
        var errors = new List<string>();
        var to = ParseRecipients(r.To, errors);

        var subject = (r.Subject ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        if (subject.Length == 0) errors.Add("Subject is required.");
        if (subject.Length > 300) subject = subject[..300];

        var body = r.Body ?? "";
        if (body.Length == 0) errors.Add("Body is required.");
        if (body.Length > 200_000) errors.Add("Body is too large (max 200,000 characters).");

        if (errors.Count > 0)
            throw new NotifyException(400, "Email validation failed.", errors);

        var log = new NotificationLog
        {
            ToList = string.Join(";", to),
            Subject = subject,
            Body = body,
            IsHtml = r.IsHtml ?? false
        };
        _db.Notifications.Add(log);
        await _db.SaveChangesAsync(ct);

        await DeliverAsync(log, ct);
        return log;
    }

    // ---------------------------------------------------------------- (re)try delivery of a logged email
    public async Task DeliverAsync(NotificationLog log, CancellationToken ct)
    {
        log.Attempts++;
        try
        {
            if (_o.DryRun)
            {
                _log.LogInformation("DRY RUN email to {To}: {Subject}", log.ToList, log.Subject);
                log.Status = NotifyStatus.DryRun;
            }
            else
            {
                await TransmitAsync(log, ct);
                log.Status = NotifyStatus.Sent;
            }
            log.Error = null;
            log.SentAt = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning("Email {Id} failed: {Msg}", log.Id, ex.Message);
            log.Status = NotifyStatus.Failed;
            log.Error = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task TransmitAsync(NotificationLog log, CancellationToken ct)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(_o.FromName, _o.FromAddress));
        foreach (var a in log.ToList.Split(';', StringSplitOptions.RemoveEmptyEntries))
            msg.To.Add(MailboxAddress.Parse(a));
        msg.Subject = log.Subject;

        var builder = new BodyBuilder();
        if (log.IsHtml) builder.HtmlBody = log.Body; else builder.TextBody = log.Body;
        msg.Body = builder.ToMessageBody();

        var security = Enum.TryParse<SecureSocketOptions>(_o.Security, true, out var s)
            ? s
            : SecureSocketOptions.StartTls;

        using var client = new SmtpClient { Timeout = _o.TimeoutSeconds * 1000 };
        await client.ConnectAsync(_o.Host, _o.Port, security, ct);
        if (!string.IsNullOrWhiteSpace(_o.Username))
            await client.AuthenticateAsync(_o.Username, _o.Password ?? "", ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
    }

    // ---------------------------------------------------------------- recipients
    private List<string> ParseRecipients(string[]? raw, List<string> errors)
    {
        var result = new List<string>();
        // Accept "a@x.com; b@y.com" inside one item as well as separate items
        var items = (raw ?? Array.Empty<string>())
            .SelectMany(x => (x ?? "").Split(new[] { ';', ',' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (items.Count == 0) { errors.Add("At least one recipient is required."); return result; }
        if (items.Count > _o.MaxRecipients)
        {
            errors.Add($"Too many recipients (max {_o.MaxRecipients}).");
            return result;
        }

        foreach (var item in items)
        {
            if (!MailboxAddress.TryParse(item, out var box) || box is not MailboxAddress mb
                || !mb.Address.Contains('@'))
            {
                errors.Add($"'{item}' is not a valid email address.");
                continue;
            }

            if (_o.AllowedDomains.Length > 0)
            {
                var domain = mb.Address[(mb.Address.LastIndexOf('@') + 1)..];
                if (!_o.AllowedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add($"Domain '{domain}' is not allowed.");
                    continue;
                }
            }
            result.Add(mb.Address);
        }
        return result;
    }
}