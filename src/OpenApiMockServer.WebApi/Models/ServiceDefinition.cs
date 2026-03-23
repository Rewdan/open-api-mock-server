namespace OpenApiMockServer.WebApi.Models;

public class ServiceDefinition
{
    public string OpenApiUrl { get; set; } = string.Empty;
    public string? BasePath { get; set; }
    public Dictionary<string, object>? CustomData { get; set; }
}
