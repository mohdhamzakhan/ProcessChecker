namespace DefinitionService;

public class DefinitionEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public int Version { get; set; }
    public bool IsActive { get; set; } = true;
    public string JsonBody { get; set; } = "";     // serialized ProcessDefinitionDto
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}