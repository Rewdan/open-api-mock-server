namespace OpenApiMockServer.WebApi.Models;

public class ServiceConfig
{
    public Dictionary<string, ServiceDefinition> Services { get; set; } = new();
}
