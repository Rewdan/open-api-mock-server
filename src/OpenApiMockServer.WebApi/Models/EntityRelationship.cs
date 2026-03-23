namespace OpenApiMockServer.WebApi.Models;

public class EntityRelationship
{
    public string SourceEntity { get; set; } = string.Empty;
    public string SourceProperty { get; set; } = string.Empty;
    public string TargetEntity { get; set; } = string.Empty;
    public string TargetProperty { get; set; } = string.Empty;
    public bool IsCollection { get; set; }
}
