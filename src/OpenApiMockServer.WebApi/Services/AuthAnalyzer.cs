using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Interfaces;
using OpenApiMockServer.WebApi.Models;
using System.Text.Json;
using OpenApiMockServer.WebApi.OAuth2;

namespace OpenApiMockServer.WebApi.Services;

public class AuthAnalyzer
{
    private readonly ILogger<AuthAnalyzer> _logger;

    public AuthAnalyzer(ILogger<AuthAnalyzer> logger)
    {
        _logger = logger;
    }

    public AuthRequirements Analyze(OpenApiDocument document)
    {
        var requirements = new AuthRequirements();

        if (document == null)
        {
            return requirements;
        }

        // Извлекаем security schemes (OpenAPI 3.0)
        ExtractSecuritySchemes(document.Components?.SecuritySchemes, requirements);

        // Извлекаем securityDefinitions (Swagger 2.0)
        ExtractSwagger2SecurityDefinitions(document, requirements);

        // Анализируем security requirements для каждого пути
        if (document.Paths != null)
        {
            foreach (var path in document.Paths)
            {
                foreach (var operation in path.Value.Operations)
                {
                    var pathKey = $"{operation.Key}_{path.Key}";
                    var schemes = new List<string>();

                    // Проверяем security на уровне операции
                    if (operation.Value.Security != null && operation.Value.Security.Any())
                    {
                        foreach (var securityReq in operation.Value.Security)
                        {
                            foreach (var scheme in securityReq)
                            {
                                var schemeName = GetSchemeName(scheme.Key);
                                schemes.Add(schemeName);
                                _logger.LogDebug($"Operation {operation.Key} {path.Key} requires scheme: {schemeName}");
                            }
                        }
                    }
                    // Проверяем глобальную security
                    else if (document.SecurityRequirements != null && document.SecurityRequirements.Any())
                    {
                        foreach (var securityReq in document.SecurityRequirements)
                        {
                            foreach (var scheme in securityReq)
                            {
                                var schemeName = GetSchemeName(scheme.Key);
                                schemes.Add(schemeName);
                                _logger.LogDebug($"Global security requires scheme: {schemeName}");
                            }
                        }
                    }

                    if (schemes.Any())
                    {
                        requirements.RequiresAuth = true;
                        requirements.PathRequirements[pathKey] = schemes.Distinct().ToList();
                        _logger.LogInformation($"Path {pathKey} requires auth: {string.Join(", ", schemes)}");
                    }
                }
            }
        }

        // Для Swagger 2.0, также проверяем security в расширениях операций
        ExtractSwagger2SecurityRequirements(document, requirements);

        // Определяем публичные пути
        DeterminePublicPaths(document, requirements);

        return requirements;
    }

    private void ExtractSecuritySchemes(
        IDictionary<string, OpenApiSecurityScheme>? securitySchemes,
        AuthRequirements requirements)
    {
        if (securitySchemes == null) return;

        foreach (var scheme in securitySchemes)
        {
            var authScheme = new AuthScheme
            {
                Name = scheme.Key,
                Type = scheme.Value.Type.ToString(),
                Scheme = scheme.Value.Scheme,
                In = scheme.Value.In.ToString(),
                NameInRequest = scheme.Value.Name,
                Description = scheme.Value.Description
            };

            if (scheme.Value.Flows != null)
            {
                authScheme.Flows = new AuthFlows();

                if (scheme.Value.Flows.Implicit != null)
                {
                    authScheme.Flows.Implicit = new OAuthFlow
                    {
                        AuthorizationUrl = scheme.Value.Flows.Implicit.AuthorizationUrl?.ToString(),
                        TokenUrl = scheme.Value.Flows.Implicit.TokenUrl?.ToString(),
                        Scopes = scheme.Value.Flows.Implicit.Scopes.ToDictionary()
                    };
                    authScheme.Flow = "implicit";
                }

                if (scheme.Value.Flows.AuthorizationCode != null)
                {
                    authScheme.Flows.AuthorizationCode = new OAuthFlow
                    {
                        AuthorizationUrl = scheme.Value.Flows.AuthorizationCode.AuthorizationUrl?.ToString(),
                        TokenUrl = scheme.Value.Flows.AuthorizationCode.TokenUrl?.ToString(),
                        Scopes = scheme.Value.Flows.AuthorizationCode.Scopes.ToDictionary()
                    };
                    authScheme.Flow = "authorizationCode";
                }

                authScheme.Scopes = ExtractScopesFromFlows(scheme.Value.Flows);
            }

            requirements.Schemes.Add(authScheme);
            _logger.LogDebug($"Extracted security scheme: {authScheme.Name} (type: {authScheme.Type})");
        }
    }

    private void ExtractSwagger2SecurityDefinitions(OpenApiDocument document, AuthRequirements requirements)
    {
        if (document.Extensions != null &&
            document.Extensions.TryGetValue("securityDefinitions", out var securityDefsExtension))
        {
            try
            {
                var securityDefsJson = securityDefsExtension.ToString();
                using var doc = JsonDocument.Parse(securityDefsJson);

                foreach (var def in doc.RootElement.EnumerateObject())
                {
                    var schemeName = def.Name;
                    var schemeType = def.Value.GetProperty("type").GetString() ?? "unknown";

                    var authScheme = new AuthScheme
                    {
                        Name = schemeName,
                        Type = schemeType
                    };

                    if (schemeType == "apiKey")
                    {
                        authScheme.In = def.Value.GetProperty("in").GetString();
                        authScheme.NameInRequest = def.Value.GetProperty("name").GetString();
                    }
                    else if (schemeType == "oauth2")
                    {
                        authScheme.AuthorizationUrl = def.Value.GetProperty("authorizationUrl").GetString();
                        authScheme.Flow = def.Value.GetProperty("flow").GetString();

                        if (def.Value.TryGetProperty("scopes", out var scopes))
                        {
                            foreach (var scope in scopes.EnumerateObject())
                            {
                                authScheme.Scopes.Add(scope.Name);
                            }
                        }
                    }

                    requirements.Schemes.Add(authScheme);
                    _logger.LogDebug($"Extracted Swagger 2.0 security definition: {schemeName} (type: {schemeType})");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing securityDefinitions");
            }
        }
    }

    private void ExtractSwagger2SecurityRequirements(OpenApiDocument document, AuthRequirements requirements)
    {
        if (document.Paths == null) return;

        foreach (var path in document.Paths)
        {
            foreach (var operation in path.Value.Operations)
            {
                var pathKey = $"{operation.Key}_{path.Key}";

                // Проверяем расширения операции для Swagger 2.0 security
                if (operation.Value.Extensions != null &&
                    operation.Value.Extensions.TryGetValue("security", out var securityExtension))
                {
                    try
                    {
                        var securityJson = securityExtension.ToString();
                        using var doc = JsonDocument.Parse(securityJson);

                        var schemes = new List<string>();

                        if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var securityItem in doc.RootElement.EnumerateArray())
                            {
                                foreach (var property in securityItem.EnumerateObject())
                                {
                                    schemes.Add(property.Name);
                                    _logger.LogDebug($"Found Swagger 2.0 security requirement: {property.Name} for {pathKey}");
                                }
                            }
                        }

                        if (schemes.Any())
                        {
                            requirements.RequiresAuth = true;
                            if (requirements.PathRequirements.ContainsKey(pathKey))
                            {
                                requirements.PathRequirements[pathKey].AddRange(schemes);
                            }
                            else
                            {
                                requirements.PathRequirements[pathKey] = schemes;
                            }
                            _logger.LogInformation($"Path {pathKey} requires auth (Swagger 2.0): {string.Join(", ", schemes)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Error parsing security extension for {pathKey}");
                    }
                }
            }
        }
    }

    private string GetSchemeName(OpenApiSecurityScheme scheme)
    {
        if (scheme.Reference != null)
        {
            return scheme.Reference.Id;
        }
        return scheme.Type.ToString();
    }

    private List<string> ExtractScopesFromFlows(OpenApiOAuthFlows flows)
    {
        var scopes = new List<string>();

        if (flows.Implicit?.Scopes != null)
            scopes.AddRange(flows.Implicit.Scopes.Keys);
        if (flows.Password?.Scopes != null)
            scopes.AddRange(flows.Password.Scopes.Keys);
        if (flows.ClientCredentials?.Scopes != null)
            scopes.AddRange(flows.ClientCredentials.Scopes.Keys);
        if (flows.AuthorizationCode?.Scopes != null)
            scopes.AddRange(flows.AuthorizationCode.Scopes.Keys);

        return scopes.Distinct().ToList();
    }

    private void DeterminePublicPaths(OpenApiDocument document, AuthRequirements requirements)
    {
        var defaultPublicPaths = new[]
        {
            "/health", "/healthz", "/ready", "/status", "/ping",
            "/openapi", "/swagger", "/swagger-ui", "/swagger.json",
            "/api-docs", "/api-docs/swagger.json"
        };

        requirements.PublicPaths.AddRange(defaultPublicPaths);
    }

    public List<string> GetRequiredAuthSchemesForPath(string path, string method, AuthRequirements requirements)
    {
        var normalizedPath = path.Split('?')[0].TrimEnd('/');
        var pathKey = $"{method}_{normalizedPath}";

        _logger.LogDebug($"Getting auth schemes for path: {pathKey}");
        _logger.LogDebug($"Available path requirements: {string.Join(", ", requirements.PathRequirements.Keys)}");

        // Прямое совпадение
        if (requirements.PathRequirements.TryGetValue(pathKey, out var schemes))
        {
            _logger.LogDebug($"Found exact match for {pathKey}: {string.Join(", ", schemes)}");
            return schemes;
        }

        // Проверяем пути с параметрами (например, /pet/{petId})
        foreach (var reqPath in requirements.PathRequirements.Keys)
        {
            var reqMethod = reqPath.Split('_')[0];
            var reqPathPattern = reqPath.Substring(reqMethod.Length + 1);

            if (reqMethod == method && IsPathMatch(normalizedPath, reqPathPattern))
            {
                _logger.LogDebug($"Found pattern match: {reqPathPattern} -> schemes: {string.Join(", ", requirements.PathRequirements[reqPath])}");
                return requirements.PathRequirements[reqPath];
            }
        }

        // Проверяем глобальные требования
        if (requirements.RequiresAuth)
        {
            // Возвращаем все доступные схемы как требуемые
            var allSchemes = requirements.Schemes.Select(s => s.Name).ToList();
            _logger.LogDebug($"Using global auth requirement with all schemes: {string.Join(", ", allSchemes)}");
            return allSchemes;
        }

        _logger.LogDebug($"No auth required for {pathKey}");
        return new List<string>();
    }

    private bool IsPathMatch(string actualPath, string patternPath)
    {
        var actualSegments = actualPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var patternSegments = patternPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (actualSegments.Length != patternSegments.Length)
        {
            return false;
        }

        for (int i = 0; i < actualSegments.Length; i++)
        {
            if (patternSegments[i].StartsWith("{") && patternSegments[i].EndsWith("}"))
            {
                continue;
            }
            if (actualSegments[i] != patternSegments[i])
            {
                return false;
            }
        }

        return true;
    }

    public string GetAuthDescription(AuthRequirements requirements)
    {
        if (!requirements.RequiresAuth)
        {
            return "🔓 Открытый API - Авторизация не требуется";
        }

        var descriptions = new List<string>();
        descriptions.Add("🔐 Требуется авторизация");

        foreach (var scheme in requirements.Schemes)
        {
            var schemeDesc = scheme.Type.ToLower() switch
            {
                "apikey" when scheme.In == "header" => $"API Key (Header: {scheme.NameInRequest})",
                "apikey" when scheme.In == "query" => $"API Key (Query: {scheme.NameInRequest})",
                "apikey" when scheme.In == "cookie" => $"API Key (Cookie: {scheme.NameInRequest})",
                "http" when scheme.Scheme?.ToLower() == "bearer" => "Bearer Token (JWT)",
                "http" when scheme.Scheme?.ToLower() == "basic" => "Basic Authentication",
                "http" => $"HTTP Authentication ({scheme.Scheme})",
                "oauth2" when scheme.Flow == "implicit" => "OAuth2 Implicit Flow",
                "oauth2" when scheme.Flow == "password" => "OAuth2 Password Flow",
                "oauth2" when scheme.Flow == "clientCredentials" => "OAuth2 Client Credentials Flow",
                "oauth2" when scheme.Flow == "authorizationCode" => "OAuth2 Authorization Code Flow",
                "oauth2" => "OAuth2",
                _ => $"{scheme.Type} ({scheme.Scheme})"
            };

            if (scheme.Scopes.Any())
            {
                schemeDesc += $"\n    Scopes: {string.Join(", ", scheme.Scopes)}";
            }

            descriptions.Add($"- {schemeDesc}");
        }

        return string.Join("\n", descriptions);
    }

    public string GetAuthExample(AuthRequirements requirements, string baseUrl)
    {
        if (!requirements.RequiresAuth)
        {
            return "Авторизация не требуется";
        }

        var examples = new List<string>();

        foreach (var scheme in requirements.Schemes)
        {
            var example = scheme.Type.ToLower() switch
            {
                "apikey" when scheme.In == "header" => $"{scheme.NameInRequest ?? "X-API-Key"}: your-api-key-here",
                "apikey" when scheme.In == "query" => $"?{scheme.NameInRequest ?? "api_key"}=your-api-key-here",
                "apikey" when scheme.In == "cookie" => $"{scheme.NameInRequest ?? "api_key"}=your-api-key-here",
                "http" when scheme.Scheme?.ToLower() == "bearer" => "Authorization: Bearer valid-token-123",
                "http" when scheme.Scheme?.ToLower() == "basic" => "Authorization: Basic base64(username:password)",
                "oauth2" => $"OAuth2 Flow: {scheme.Flow ?? "authorization_code"}\n" +
                            $"Authorization URL: {baseUrl}/oauth/authorize\n" +
                            $"Token URL: {baseUrl}/oauth/token\n" +
                            $"Client ID: test-client\n" +
                            $"Client Secret: test-secret\n" +
                            $"Scopes: {string.Join(", ", scheme.Scopes)}",
                _ => "Проверьте документацию"
            };
            examples.Add(example);
        }

        return string.Join("\n", examples);
    }
}