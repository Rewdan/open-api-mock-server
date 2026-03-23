using Microsoft.OpenApi.Models;
namespace OpenApiMockServer.WebApi.Models;

public class ServiceInfo
{
    public string Name { get; set; } = string.Empty;
    public string OpenApiUrl { get; set; } = string.Empty;
    public string BasePath { get; set; } = string.Empty;
    public bool IsConfigured { get; set; }
    public string? ConfigurationError { get; set; }
    public OpenApiDocument? OpenApiDocument { get; set; }
    public OpenApiDocument? ModifiedOpenApiDocument { get; set; }
    public MockDataStore DataStore { get; set; } = new();
    public AuthRequirements AuthRequirements { get; set; } = new();
}
