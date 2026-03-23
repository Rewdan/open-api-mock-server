namespace OpenApiMockServer.WebApi.OAuth2;

public class OAuthFlow
{
    public string? AuthorizationUrl { get; set; }
    public string? TokenUrl { get; set; }
    public string? RefreshUrl { get; set; }
    public Dictionary<string, string>? Scopes { get; set; }
}
