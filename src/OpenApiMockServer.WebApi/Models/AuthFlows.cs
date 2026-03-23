using OpenApiMockServer.WebApi.OAuth2;

namespace OpenApiMockServer.WebApi.Models;

public class AuthFlows
{
    public OAuthFlow? Implicit { get; set; }
    public OAuthFlow? Password { get; set; }
    public OAuthFlow? ClientCredentials { get; set; }
    public OAuthFlow? AuthorizationCode { get; set; }
}
