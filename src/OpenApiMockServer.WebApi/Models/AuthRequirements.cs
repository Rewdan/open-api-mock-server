namespace OpenApiMockServer.WebApi.Models;

public class AuthRequirements
{
    public bool RequiresAuth { get; set; }
    public List<AuthScheme> Schemes { get; set; } = new();
    public List<string> PublicPaths { get; set; } = new();
    public Dictionary<string, List<string>> PathRequirements { get; set; } = new();
}