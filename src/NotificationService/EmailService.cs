using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace NotificationService;

public record MailPart(string Name, string Type, byte[] Bytes);

public class EmailService
{
    private readonly NotifyDb _db;
    private readonly SmtpOptions _o;
    private readonly EmailBrand _brand;
    private readonly ILogger<EmailService> _log;

    public EmailService(NotifyDb db, SmtpOptions options, EmailBrand brand, ILogger<EmailService> log)
    {
        _db = db; _o = options; _brand = brand; _log = log;
    }

    public async Task<NotificationLog> SendAsync(EmailRequest r, CancellationToken ct)
    {
        var errors = new List<string>();
        var to = ParseRecipients(r.To, errors);

        var subject = (r.Subject ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        if (subject.Length == 0) errors.Add("Subject is required.");
        if (subject.Length > 300) subject = subject[..300];

        var body = r.Body ?? "";
        if (r.Template is null && body.Length == 0) errors.Add("Body is required.");
        if (body.Length > 200_000) errors.Add("Body is too large (max 200,000 characters).");

        var files = ParseAttachments(r.Attachments, errors);
        if (errors.Count > 0) throw new NotifyException(400, "Email validation failed.", errors);

        string finalBody;
        bool isHtml;
        if (r.Template is not null)
        {
            finalBody = EmailTemplates.Render(_brand, subject, r.Template);
            isHtml = true;
        }
        else if (_brand.UseLayout)
        {
            finalBody = EmailTemplates.Wrap(_brand, subject, body, r.IsHtml ?? false);
            isHtml = true;
        }
        else
        {
            finalBody = body;
            isHtml = r.IsHtml ?? false;
        }

        var names = string.Join("; ", files.Select(f => f.Name));
        var log = new NotificationLog
        {
            ToList = string.Join(";", to),
            Subject = subject,
            Body = finalBody,
            IsHtml = isHtml,
            AttachmentNames = files.Count == 0 ? null : names.Length > 1000 ? names[..1000] : names
        };
        _db.Notifications.Add(log);
        await _db.SaveChangesAsync(ct);

        await DeliverAsync(log, files, ct);
        return log;
    }

    public async Task DeliverAsync(NotificationLog log, IReadOnlyList<MailPart>? files, CancellationToken ct)
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
                await TransmitAsync(log, files, ct);
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

    private async Task TransmitAsync(NotificationLog log, IReadOnlyList<MailPart>? files, CancellationToken ct)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(_o.FromName, _o.FromEmail));
        foreach (var a in log.ToList.Split(';', StringSplitOptions.RemoveEmptyEntries))
            msg.To.Add(MailboxAddress.Parse(a));
        msg.Subject = log.Subject;

        var builder = new BodyBuilder();
        if (log.IsHtml)
        {
            builder.HtmlBody = log.Body;
            builder.TextBody = EmailTemplates.HtmlToText(log.Body);     // the plain-text version
        }
        else builder.TextBody = log.Body;

        foreach (var f in files ?? Array.Empty<MailPart>())
            builder.Attachments.Add(f.Name, f.Bytes,
                ContentType.TryParse(f.Type, out var parsed) ? parsed : new ContentType("application", "octet-stream"));
        msg.Body = builder.ToMessageBody();

        SecureSocketOptions security;
        if (!string.IsNullOrWhiteSpace(_o.Security) && Enum.TryParse<SecureSocketOptions>(_o.Security, true, out var forced))
            security = forced;
        else if (!_o.EnableSsl) security = SecureSocketOptions.None;
        else security = _o.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

        using var client = new SmtpClient { Timeout = _o.TimeoutSeconds * 1000 };
        await client.ConnectAsync(_o.Host, _o.Port, security, ct);
        if (!string.IsNullOrWhiteSpace(_o.Username))
            await client.AuthenticateAsync(_o.Username, _o.Password ?? "", ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
    }

    private List<MailPart> ParseAttachments(List<AttachmentDto>? items, List<string> errors)
    {
        var list = new List<MailPart>();
        if (items is null || items.Count == 0) return list;
        if (items.Count > 10) { errors.Add("At most 10 attachments are allowed."); return list; }

        long total = 0;
        foreach (var a in items)
        {
            var name = SafeName(a.FileName);
            byte[] bytes;
            try { bytes = Convert.FromBase64String(a.Base64 ?? ""); }
            catch (FormatException) { errors.Add($"Attachment '{name}' is not valid."); continue; }
            if (bytes.Length == 0) { errors.Add($"Attachment '{name}' is empty."); continue; }
            total += bytes.Length;
            list.Add(new MailPart(name, string.IsNullOrWhiteSpace(a.ContentType) ? "application/octet-stream" : a.ContentType!, bytes));
        }
        if (total > _o.MaxAttachmentMb * 1024L * 1024L) errors.Add($"The attachments are larger than {_o.MaxAttachmentMb} MB.");
        return list;
    }

    private static string SafeName(string? raw)
    {
        var name = Path.GetFileName((raw ?? "file").Replace('\\', '/'));
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c.ToString(), "");
        name = name.Trim();
        return name.Length == 0 ? "file" : name.Length > 150 ? name[..150] : name;
    }

    private List<string> ParseRecipients(string[]? raw, List<string> errors)
    {
        var result = new List<string>();
        var items = (raw ?? Array.Empty<string>())
            .SelectMany(x => (x ?? "").Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (items.Count == 0) { errors.Add("At least one recipient is required."); return result; }
        if (items.Count > _o.MaxRecipients) { errors.Add($"Too many recipients (max {_o.MaxRecipients})."); return result; }

        foreach (var item in items)
        {
            if (!MailboxAddress.TryParse(item, out var box) || box is not MailboxAddress mb || !mb.Address.Contains('@'))
            { errors.Add($"'{item}' is not a valid email address."); continue; }

            if (_o.AllowedDomains.Length > 0)
            {
                var domain = mb.Address[(mb.Address.LastIndexOf('@') + 1)..];
                if (!_o.AllowedDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
                { errors.Add($"Domain '{domain}' is not allowed."); continue; }
            }
            result.Add(mb.Address);
        }
        return result;
    }
}