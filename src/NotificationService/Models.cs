namespace NotificationService;

public static class NotifyStatus
{
    public const string Sent = "Sent";
    public const string Failed = "Failed";
    public const string DryRun = "DryRun";
}

public class NotificationLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ToList { get; set; } = "";        // addresses joined by ';'
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsHtml { get; set; }
    public string Status { get; set; } = NotifyStatus.Failed;
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
}

public class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 587;
    public string Security { get; set; } = "StartTls";   // StartTls | SslOnConnect | None | Auto
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "noreply@example.com";
    public string FromName { get; set; } = "ProcessChecker";
    public int TimeoutSeconds { get; set; } = 30;
    public bool DryRun { get; set; }
    public string[] AllowedDomains { get; set; } = Array.Empty<string>();   // empty = allow all
    public int MaxRecipients { get; set; } = 50;
}

public record EmailRequest(string[]? To, string? Subject, string? Body, bool? IsHtml);
public record TestRequest(string To);

public class NotifyException : Exception
{
    public int Status { get; }
    public List<string> Errors { get; }

    public NotifyException(int status, string message, List<string>? errors = null) : base(message)
    {
        Status = status;
        Errors = errors ?? new List<string>();
    }
}