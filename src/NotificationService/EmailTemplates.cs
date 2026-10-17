using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace NotificationService;

public class EmailBrand
{
    public string AppName { get; set; } = "ProcessChecker";
    public string? Footer { get; set; }       // e.g. "MEAI India · This is an automatic message, please do not reply."
    public string? LinkUrl { get; set; }      // the address of the web app; shown in the footer as "Open ProcessChecker"
    public bool UseLayout { get; set; } = true;
}

public record TemplateDetail(string Label, string? Value);
public record TemplateItem(string Title, string? Sub = null, string? Meta = null, string? Url = null, string? Badge = null);

public class EmailTemplateDto
{
    public string Kind { get; set; } = "info";
    public string? Label { get; set; }          // replaces the coloured label text
    public string? Title { get; set; }
    public string? Intro { get; set; }
    public string? Message { get; set; }        // free text, line breaks and web addresses are kept
    public List<TemplateDetail>? Details { get; set; }
    public List<TemplateItem>? Items { get; set; }
    public string? ButtonLabel { get; set; }
    public string? ButtonUrl { get; set; }
    public string? Note { get; set; }
}

public static class EmailTemplates
{
    private const string Font = "'Segoe UI',Roboto,Helvetica,Arial,sans-serif";
    private static readonly Regex UrlRx = new(@"https?://[^\s<>""]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// kind -> (label shown in the email, colour). Change the colours here.
    public static readonly Dictionary<string, (string Label, string Color)> Kinds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["task"] = ("Task assigned", "#4f46e5"),
            ["approval"] = ("Approval needed", "#7c3aed"),
            ["reminder"] = ("Reminder", "#d97706"),
            ["overdue"] = ("Overdue", "#dc2626"),
            ["escalated"] = ("Escalated", "#ea580c"),
            ["deadline"] = ("Deadline passed", "#b45309"),
            ["completed"] = ("Completed", "#16a34a"),
            ["rejected"] = ("Rejected", "#dc2626"),
            ["cancelled"] = ("Cancelled", "#64748b"),
            ["observer"] = ("You are observing", "#0891b2"),
            ["file"] = ("New file", "#2563eb"),
            ["dispatch"] = ("New request", "#0d9488"),
            ["success"] = ("Good news", "#16a34a"),
            ["warning"] = ("Please note", "#d97706"),
            ["alert"] = ("Important", "#dc2626"),
            ["info"] = ("Update", "#4f46e5"),
            ["test"] = ("Test email", "#64748b"),
        };

    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static bool IsHttp(string? u) =>
        Uri.TryCreate(u, UriKind.Absolute, out var x) && (x.Scheme == Uri.UriSchemeHttp || x.Scheme == Uri.UriSchemeHttps);

    /// A very light version of a colour, used behind labels and badges.
    private static string Tint(string hex)
    {
        int Ch(int i) => Convert.ToInt32(hex.Substring(1 + i * 2, 2), 16);
        string Mix(int c) => ((int)Math.Round(255 - (255 - c) * 0.12)).ToString("x2");
        return "#" + Mix(Ch(0)) + Mix(Ch(1)) + Mix(Ch(2));
    }

    /// Plain text to safe HTML: line breaks are kept and web addresses become links.
    private static string Rich(string text)
    {
        text = text.Replace("\r\n", "\n");
        var sb = new StringBuilder();
        var last = 0;
        foreach (Match m in UrlRx.Matches(text))
        {
            sb.Append(Enc(text[last..m.Index]));
            var url = m.Value.TrimEnd('.', ',', ';', ':', ')', '!', '?');
            var rest = m.Value[url.Length..];
            if (IsHttp(url)) sb.Append($"<a href=\"{Enc(url)}\" style=\"color:#4f46e5;word-break:break-all;\">{Enc(url)}</a>");
            else sb.Append(Enc(url));
            sb.Append(Enc(rest));
            last = m.Index + m.Length;
        }
        sb.Append(Enc(text[last..]));
        return sb.ToString().Replace("\n", "<br/>");
    }

    public static string Render(EmailBrand brand, string subject, EmailTemplateDto t)
    {
        var kind = Kinds.TryGetValue(t.Kind ?? "info", out var k) ? k : Kinds["info"];
        var color = kind.Color;
        var label = string.IsNullOrWhiteSpace(t.Label) ? kind.Label : t.Label!.Trim();

        string Para(string text) =>
            $"<p style=\"margin:14px 0 0;font:400 15px/1.65 {Font};color:#334155;\">{Rich(text)}</p>";

        var p = new StringBuilder();
        p.Append($"<div style=\"display:inline-block;padding:4px 12px;border-radius:999px;background:{Tint(color)};color:{color};font:700 12px/1.4 {Font};letter-spacing:.06em;text-transform:uppercase;\">{Enc(label)}</div>");
        if (!string.IsNullOrWhiteSpace(t.Title))
            p.Append($"<h1 style=\"margin:14px 0 0;font:700 22px/1.3 {Font};color:#0f172a;\">{Enc(t.Title)}</h1>");
        if (!string.IsNullOrWhiteSpace(t.Intro)) p.Append(Para(t.Intro!));
        if (!string.IsNullOrWhiteSpace(t.Message)) p.Append(Para(t.Message!));

        var rows = (t.Details ?? new List<TemplateDetail>())
            .Where(d => !string.IsNullOrWhiteSpace(d.Label)).ToList();
        if (rows.Count > 0)
        {
            p.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin-top:18px;border-top:1px solid #e5e9f2;\">");
            foreach (var d in rows)
                p.Append($"<tr><td style=\"padding:9px 12px 9px 0;width:36%;border-bottom:1px solid #e5e9f2;font:600 13px/1.5 {Font};color:#64748b;vertical-align:top;\">{Enc(d.Label)}</td>" +
                         $"<td style=\"padding:9px 0;border-bottom:1px solid #e5e9f2;font:400 14px/1.5 {Font};color:#0f172a;vertical-align:top;\">{(string.IsNullOrWhiteSpace(d.Value) ? "&ndash;" : Rich(d.Value!))}</td></tr>");
            p.Append("</table>");
        }

        foreach (var it in t.Items ?? new List<TemplateItem>())
        {
            var c = string.Equals(it.Badge, "Overdue", StringComparison.OrdinalIgnoreCase) ? "#dc2626" : color;
            var title = IsHttp(it.Url)
                ? $"<a href=\"{Enc(it.Url)}\" style=\"color:#0f172a;text-decoration:none;\">{Enc(it.Title)}</a>"
                : Enc(it.Title);
            var badge = string.IsNullOrWhiteSpace(it.Badge) ? "" :
                $"<span style=\"display:inline-block;margin-left:8px;padding:1px 9px;border-radius:999px;background:{Tint(c)};color:{c};font:700 11px/1.6 {Font};text-transform:uppercase;letter-spacing:.04em;\">{Enc(it.Badge)}</span>";
            p.Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin-top:10px;border:1px solid #e5e9f2;border-left:4px solid {c};border-radius:8px;\"><tr><td style=\"padding:12px 14px;\">");
            p.Append($"<div style=\"font:700 15px/1.4 {Font};color:#0f172a;\">{title}{badge}</div>");
            if (!string.IsNullOrWhiteSpace(it.Sub)) p.Append($"<div style=\"margin-top:2px;font:400 13px/1.5 {Font};color:#64748b;\">{Enc(it.Sub)}</div>");
            if (!string.IsNullOrWhiteSpace(it.Meta)) p.Append($"<div style=\"margin-top:2px;font:400 12px/1.5 {Font};color:#94a3b8;\">{Enc(it.Meta)}</div>");
            p.Append("</td></tr></table>");
        }

        if (!string.IsNullOrWhiteSpace(t.Note))
            p.Append($"<p style=\"margin:16px 0 0;font:400 13px/1.6 {Font};color:#64748b;\">{Rich(t.Note!)}</p>");

        if (IsHttp(t.ButtonUrl))
        {
            var bl = string.IsNullOrWhiteSpace(t.ButtonLabel) ? "Open" : t.ButtonLabel!.Trim();
            p.Append($"<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin-top:24px;\"><tr><td bgcolor=\"{color}\" style=\"border-radius:8px;background:{color};\">" +
                     $"<a href=\"{Enc(t.ButtonUrl)}\" style=\"display:inline-block;padding:12px 24px;font:600 15px/1.2 {Font};color:#ffffff;text-decoration:none;border-radius:8px;\">{Enc(bl)}</a></td></tr></table>");
            p.Append($"<p style=\"margin:14px 0 0;font:400 12px/1.5 {Font};color:#94a3b8;\">If the button does not work, copy this address into your browser:<br/>" +
                     $"<a href=\"{Enc(t.ButtonUrl)}\" style=\"color:{color};word-break:break-all;\">{Enc(t.ButtonUrl)}</a></p>");
        }

        var pre = new[] { t.Intro, t.Message, t.Title, subject }.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
        return Layout(brand, subject, color, p.ToString(), pre.Length > 120 ? pre[..120] : pre);
    }

    /// An email that has no template: plain text gets the full design, ready-made HTML goes inside the card.
    public static string Wrap(EmailBrand brand, string subject, string body, bool isHtml)
    {
        if (!isHtml)
            return Render(brand, subject, new EmailTemplateDto { Kind = "info", Title = subject, Message = body });

        var inner = $"<div style=\"font:400 15px/1.65 {Font};color:#334155;\">{body}</div>";
        return Layout(brand, subject, Kinds["info"].Color, inner, subject);
    }

    private static string Layout(EmailBrand b, string subject, string color, string inner, string pre)
    {
        var footer = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(b.Footer)) footer.Append(Enc(b.Footer));
        if (IsHttp(b.LinkUrl))
        {
            if (footer.Length > 0) footer.Append(" &middot; ");
            footer.Append($"<a href=\"{Enc(b.LinkUrl)}\" style=\"color:#64748b;\">Open {Enc(b.AppName)}</a>");
        }

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"/>");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"/>");
        sb.Append("<meta name=\"color-scheme\" content=\"light only\"/><meta name=\"supported-color-schemes\" content=\"light only\"/>");
        sb.Append($"<title>{Enc(subject)}</title></head>");
        sb.Append("<body style=\"margin:0;padding:0;background:#f3f5fb;\">");
        sb.Append($"<div data-pre=\"1\" style=\"display:none;max-height:0;overflow:hidden;opacity:0;color:transparent;mso-hide:all;\">{Enc(pre)}</div>");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f3f5fb;\"><tr><td align=\"center\" style=\"padding:28px 12px;\">");
        sb.Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%;max-width:600px;\">");

        sb.Append($"<tr><td style=\"padding:0 4px 14px;font:700 16px/1.2 {Font};color:#0f172a;\">" +
                  $"<span style=\"display:inline-block;width:26px;height:26px;line-height:26px;text-align:center;border-radius:7px;background:#4f46e5;color:#ffffff;font-size:14px;vertical-align:middle;\">&#10003;</span>" +
                  $"<span style=\"vertical-align:middle;margin-left:8px;\">{Enc(b.AppName)}</span></td></tr>");

        sb.Append($"<tr><td style=\"background:#ffffff;border:1px solid #e5e9f2;border-top:4px solid {color};border-radius:12px;padding:28px 30px;\">{inner}</td></tr>");

        sb.Append($"<tr><td style=\"padding:16px 8px 0;text-align:center;font:400 12px/1.6 {Font};color:#94a3b8;\">{footer}</td></tr>");
        sb.Append("</table></td></tr></table></body></html>");
        return sb.ToString();
    }

    /// The plain-text version sent next to the HTML (also helps against spam filters).
    public static string HtmlToText(string html)
    {
        var s = Regex.Replace(html ?? "",
            @"<head>.*?</head>|<div data-pre=""1""[^>]*>.*?</div>|<(script|style)[^>]*>.*?</\1>", "",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<a\s[^>]*href=""([^""]+)""[^>]*>(.*?)</a>", m =>
        {
            var url = WebUtility.HtmlDecode(m.Groups[1].Value);
            var text = WebUtility.HtmlDecode(Regex.Replace(m.Groups[2].Value, "<[^>]+>", "")).Trim();
            return text.Length == 0 || text == url ? url : $"{text} ({url})";
        }, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<br\s*/?>|</p>|</div>|</tr>|</h\d>|</table>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</t[dh]>", "  ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]+>", "");
        s = WebUtility.HtmlDecode(s);
        s = Regex.Replace(s, @"[ \t]+\n", "\n");
        s = Regex.Replace(s, @"\n{3,}", "\n\n");
        return s.Trim();
    }

    /// Example data for every kind, used by the preview page and the test emails.
    public static List<(string Kind, string Subject, EmailTemplateDto T)> Samples(EmailBrand b)
    {
        var open = (IsHttp(b.LinkUrl) ? b.LinkUrl!.TrimEnd('/') : "https://your-server")
                   + "/instances/00000000-0000-0000-0000-000000000000";
        var list = new List<(string, string, EmailTemplateDto)>();
        void Add(string kind, string subject, EmailTemplateDto t) { t.Kind = kind; list.Add((kind, subject, t)); }
        List<TemplateDetail> Rows(params (string, string)[] r) => r.Select(x => new TemplateDetail(x.Item1, x.Item2)).ToList();

        Add("task", "Task assigned: Trip to Pune", new()
        {
            Title = "Trip to Pune",
            Intro = "A task has been assigned to you.",
            Details = Rows(("Process", "Domestic travel"), ("Step", "Trip details"), ("Due", "20 Oct 2026, 5:00 PM"), ("Assigned by", "manager@company.com")),
            ButtonLabel = "Open the task",
            ButtonUrl = open
        });
        Add("approval", "Approval needed: Trip to Pune", new()
        {
            Title = "Trip to Pune",
            Intro = "Your approval is needed on this request.",
            Details = Rows(("Process", "Domestic travel"), ("Step", "Manager approval"), ("Requested by", "test.employee@company.com"), ("Please decide by", "18 Oct 2026, 5:00 PM")),
            ButtonLabel = "Review and decide",
            ButtonUrl = open
        });
        Add("reminder", "Reminder: 3 tasks waiting for you (1 overdue)", new()
        {
            Label = "Your open tasks",
            Title = "3 tasks waiting for you",
            Intro = "1 of them is overdue.",
            Items = new()
            {
                new("Trip to Delhi", "Director approval · Domestic travel", "Due 15 Oct 2026, 10:00 AM", open, "Overdue"),
                new("Trip to Pune", "Trip details · Domestic travel", "Due 20 Oct 2026, 5:00 PM", open),
                new("Trip to Goa", "Upload the travel ticket · Domestic travel", null, open),
            },
            ButtonLabel = "Open My Tasks",
            ButtonUrl = open
        });
        Add("overdue", "Overdue: Trip to Delhi", new()
        {
            Title = "Trip to Delhi",
            Intro = "This task is overdue.",
            Details = Rows(("Process", "Domestic travel"), ("Step", "Director approval"), ("Was due", "15 Oct 2026, 10:00 AM")),
            ButtonLabel = "Open the task",
            ButtonUrl = open
        });
        Add("escalated", "Escalated: Trip to Delhi", new()
        {
            Title = "Trip to Delhi",
            Intro = "A task was not handled by its deadline and has been forwarded to you.",
            Message = "The manager did not answer in time, so the approval was forwarded to you.",
            Details = Rows(("Process", "Domestic travel"), ("Step", "Manager approval"), ("Was due", "15 Oct 2026, 10:00 AM")),
            ButtonLabel = "Open the request",
            ButtonUrl = open
        });
        Add("deadline", "Deadline passed: Trip to Delhi", new()
        {
            Title = "Trip to Delhi",
            Intro = "A task was not handled by its deadline and has been forwarded to someone else.",
            Details = Rows(("Process", "Domestic travel"), ("Step", "Manager approval"), ("Was due", "15 Oct 2026, 10:00 AM")),
            ButtonLabel = "Open the request",
            ButtonUrl = open
        });
        Add("completed", "Completed: Trip to Pune", new()
        {
            Title = "Trip to Pune",
            Intro = "This request has been completed.",
            Message = "Your trip request has finished.",
            Details = Rows(("Process", "Domestic travel"), ("Result", "Completed"), ("Started", "17 Oct 2026, 9:10 AM"), ("Finished", "19 Oct 2026, 4:45 PM")),
            ButtonLabel = "Open the request",
            ButtonUrl = open
        });
        Add("rejected", "Rejected: Trip to Dubai", new()
        {
            Title = "Trip to Dubai",
            Intro = "This request was rejected.",
            Details = Rows(("Process", "Domestic travel"), ("Result", "Rejected"), ("Reason", "Only domestic travel is handled by this process (Dubai).")),
            ButtonLabel = "Open the request",
            ButtonUrl = open
        });
        Add("cancelled", "Cancelled: Trip to Goa", new()
        {
            Title = "Trip to Goa",
            Intro = "This request was cancelled.",
            Details = Rows(("Process", "Domestic travel"), ("Result", "Cancelled")),
            ButtonLabel = "Open the request",
            ButtonUrl = open
        });
        Add("observer", "You are observing: Trip to Pune", new()
        {
            Title = "Trip to Pune",
            Intro = "You have been added as an observer. You can follow this request, but you cannot act on it.",
            Details = Rows(("Process", "Domestic travel"), ("Added by", "manager@company.com")),
            ButtonLabel = "Open the request",
            ButtonUrl = open
        });
        Add("file", "New file uploaded: Trip to Pune", new()
        {
            Title = "Trip to Pune",
            Intro = "A new file was added to this request.",
            Message = "A new file was added to your trip request.",
            Details = Rows(("Process", "Domestic travel"), ("Step", "Upload the travel ticket"), ("File", "ticket.pdf"), ("Added by", "booking@company.com")),
            ButtonLabel = "Open the request",
            ButtonUrl = open
        });
        Add("dispatch", "Bookings for Test Employee (Pune)", new()
        {
            Title = "Trip to Pune",
            Intro = "Please find the details of this request below.",
            Details = Rows(("DESTINATION", "Pune"), ("EMPLOYEE_NAME", "Test Employee"), ("START_DATE", "2026-10-25"), ("DAYS", "3")),
            Note = "Attached: ticket.pdf, hotel-voucher.pdf"
        });
        Add("success", "Your trip to Pune is booked", new()
        {
            Title = "Your trip to Pune is booked",
            Message = "Hello Test Employee,\n\nYour bookings are done.\nReport: https://server.example/Document/PrintReport/1001"
        });
        Add("warning", "Please check your trip details", new()
        {
            Title = "Please check your trip details",
            Message = "Some details look unusual. Please check them before the end of the week."
        });
        Add("alert", "Action needed today", new()
        {
            Title = "Action needed today",
            Message = "This request needs your attention today."
        });
        Add("info", "Update on your request", new()
        {
            Title = "Update on your request",
            Message = "A short update, written in your own words.\nSecond line, with a link: https://server.example/page"
        });
        Add("test", "Test email", new()
        {
            Title = "This is a test email",
            Intro = "If you can read this, the mail server and the email design are working.",
            ButtonLabel = "Open the app",
            ButtonUrl = open
        });
        return list;
    }
}