namespace DocumentService;

public static class FileRules
{
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".csv"] = "text/csv",
        [".txt"] = "text/plain"
    };

    public static string[] DefaultExtensions => Types.Keys.ToArray();
    public static bool IsKnown(string ext) => Types.ContainsKey(ext);
    public static string ContentTypeOf(string ext) => Types.TryGetValue(ext, out var t) ? t : "application/octet-stream";

    /// The first bytes must match the extension, so a renamed .exe cannot pass as a .pdf.
    public static bool HeaderMatches(string ext, byte[] h) => ext.ToLowerInvariant() switch
    {
        ".pdf" => Starts(h, 0x25, 0x50, 0x44, 0x46),                          // %PDF
        ".png" => Starts(h, 0x89, 0x50, 0x4E, 0x47),
        ".jpg" or ".jpeg" => Starts(h, 0xFF, 0xD8, 0xFF),
        ".docx" or ".xlsx" => Starts(h, 0x50, 0x4B),                          // zip container
        ".doc" or ".xls" => Starts(h, 0xD0, 0xCF, 0x11, 0xE0),
        _ => true                                                             // .csv / .txt have no signature
    };

    private static bool Starts(byte[] h, params byte[] sig) =>
        h.Length >= sig.Length && sig.Select((b, i) => h[i] == b).All(x => x);

    public static string CleanName(string? raw)
    {
        var name = Path.GetFileName((raw ?? "file").Replace('\\', '/'));
        foreach (var c in Path.GetInvalidFileNameChars().Concat(Enumerable.Range(0, 32).Select(i => (char)i)))
            name = name.Replace(c.ToString(), "");
        name = name.Trim().TrimEnd('.');
        if (name.Length == 0) name = "file";
        if (name.Length > 150)
        {
            var ext = Path.GetExtension(name);
            name = name[..(150 - ext.Length)] + ext;
        }
        return name;
    }
}

public class DocStorage
{
    private readonly string _root;

    public DocStorage(DocOptions options)
    {
        _root = Path.GetFullPath(options.StoragePath);
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    /// Files are saved as <root>/<yyyy>/<MM>/<guid>.dat so a user-supplied name never touches the disk path.
    public (string Relative, string Full) NewPath(Guid id)
    {
        var now = DateTime.UtcNow;
        var rel = Path.Combine(now.ToString("yyyy"), now.ToString("MM"), id.ToString("N") + ".dat");
        var full = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return (rel, full);
    }

    public string Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relative));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid stored path.");
        return full;
    }
}