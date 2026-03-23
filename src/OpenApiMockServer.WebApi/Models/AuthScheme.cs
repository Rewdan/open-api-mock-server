namespace OpenApiMockServer.WebApi.Models;

public class AuthScheme
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // apiKey, http, oauth2, openIdConnect
    public string? Scheme { get; set; } // Bearer, Basic
    public string? In { get; set; } // header, query, cookie
    public string? NameInRequest { get; set; } // имя параметра (например, "api_key")
    public List<string> Scopes { get; set; } = new();
    public string? Description { get; set; }
    public string? AuthorizationUrl { get; set; }
    public string? TokenUrl { get; set; }
    public string? Flow { get; set; } // implicit, password, clientCredentials, authorizationCode
    public AuthFlows? Flows { get; set; }
}
