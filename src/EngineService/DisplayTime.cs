using System.Globalization;

namespace EngineService;

/// Shows times in the configured time zone (the database stores UTC).
public class DisplayTime
{
    private readonly TimeZoneInfo _tz = TimeZoneInfo.Utc;

    public DisplayTime(IConfiguration cfg)
    {
        var id = cfg["Display:TimeZone"];
        if (string.IsNullOrWhiteSpace(id)) return;
        try { _tz = TimeZoneInfo.FindSystemTimeZoneById(id); } catch { /* unknown name: stay on UTC */ }
    }

    public string Format(DateTime? utc)
    {
        if (utc is null) return "–";
        var u = DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc);
        var text = TimeZoneInfo.ConvertTimeFromUtc(u, _tz).ToString("dd MMM yyyy, h:mm tt", CultureInfo.InvariantCulture);
        return _tz == TimeZoneInfo.Utc ? text + " UTC" : text;
    }
}