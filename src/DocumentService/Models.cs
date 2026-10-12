namespace DocumentService;

public class DocumentEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = "";      // cleaned original name
    public string StoredPath { get; set; } = "";    // relative to the storage root
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public Guid? InstanceId { get; set; }
    public string? StepKey { get; set; }
    public string? UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

public class DocOptions
{
    public string StoragePath { get; set; } = "documents";
    public int MaxSizeMb { get; set; } = 20;
    public string[] AllowedExtensions { get; set; } = Array.Empty<string>();   // empty = the default list
}

public record DocumentInfo(
    Guid Id, string FileName, string ContentType, long SizeBytes,
    Guid? InstanceId, string? StepKey, string? UploadedBy, DateTime UploadedAt);