using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using OpenApiMockServer.WebApi.Models;
using OpenApiMockServer.WebApi.OAuth2;

namespace OpenApiMockServer.WebApi.Services;

public class OAuthService : IDisposable
{
    private readonly ILogger<OAuthService> _logger;
    private readonly Dictionary<string, OAuthTokenResponse> _validTokens = new();
    private readonly Dictionary<string, string> _authorizationCodes = new();
    private readonly Dictionary<string, string> _refreshTokens = new();
    private readonly List<OAuthClient> _clients = new();
    private string _issuer = string.Empty;
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _signingKey;
    private readonly SigningCredentials _signingCredentials;
    private readonly JwtSecurityTokenHandler _tokenHandler;

    public OAuthService(ILogger<OAuthService> logger)
    {
        _logger = logger;
        _tokenHandler = new JwtSecurityTokenHandler();

        // Генерируем RSA ключ и сохраняем его в поле
        _rsa = RSA.Create(2048);
        _signingKey = new RsaSecurityKey(_rsa);
        _signingCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256);
    }

    public void Dispose()
    {
        _rsa?.Dispose();
        GC.SuppressFinalize(this);
    }

    public void InitializeClients(string baseUrl)
    {
        _clients.Clear();

        // Конфиденциальный клиент (с секретом) - поддерживает оба потока
        _clients.Add(new OAuthClient
        {
            ClientId = "test-client",
            ClientSecret = "test-secret",
            ClientName = "Test Confidential Client",
            RedirectUris = new List<string>
            {
                $"{baseUrl}/callback",
                $"{baseUrl}/swagger/oauth2-redirect.html",
                $"{baseUrl}/swagger/oauth2-redirect.html?",
                $"{baseUrl}/swagger/oauth2-redirect.html#",
                "/swagger/oauth2-redirect.html"
            },
            Scopes = new List<string> { "openid", "profile", "email", "read", "write" },
            GrantTypes = new List<string> { "authorization_code", "refresh_token", "client_credentials", "password" },
            ResponseTypes = new List<string> { "code", "token" },
            IsPublic = false
        });

        // Публичный клиент (без секрета) - для Swagger UI, поддерживает оба потока
        _clients.Add(new OAuthClient
        {
            ClientId = "public-client",
            ClientName = "Public Client (for Swagger UI)",
            RedirectUris = new List<string>
            {
                $"{baseUrl}/callback",
                $"{baseUrl}/swagger/oauth2-redirect.html",
                $"{baseUrl}/swagger/oauth2-redirect.html?",
                $"{baseUrl}/swagger/oauth2-redirect.html#",
                "/swagger/oauth2-redirect.html"
            },
            Scopes = new List<string> { "openid", "profile", "email", "read", "write" },
            GrantTypes = new List<string> { "authorization_code" },
            ResponseTypes = new List<string> { "code", "token" },
            IsPublic = true
        });

        // Клиент для Swagger UI (публичный)
        _clients.Add(new OAuthClient
        {
            ClientId = "swagger-ui",
            ClientName = "Swagger UI Client",
            RedirectUris = new List<string>
            {
                $"{baseUrl}/swagger/oauth2-redirect.html",
                $"{baseUrl}/swagger/oauth2-redirect.html?",
                $"{baseUrl}/swagger/oauth2-redirect.html#",
                "/swagger/oauth2-redirect.html"
            },
            Scopes = new List<string> { "openid", "profile", "email", "read", "write" },
            GrantTypes = new List<string> { "authorization_code" },
            ResponseTypes = new List<string> { "code", "token" },
            IsPublic = true
        });

        _issuer = $"{baseUrl}/oauth";

        _logger.LogInformation($"Initialized OAuth2 clients. Total clients: {_clients.Count}");
        foreach (var client in _clients)
        {
            _logger.LogInformation($"  - Client: {client.ClientId} (Public: {client.IsPublic}, ResponseTypes: {string.Join(",", client.ResponseTypes)})");
        }
    }

    public List<OAuthClient> GetAllClients()
    {
        return _clients.ToList();
    }

    public OAuthClient? GetClient(string clientId)
    {
        if (string.IsNullOrEmpty(clientId))
        {
            _logger.LogWarning("GetClient called with null or empty clientId");
            return null;
        }

        var client = _clients.FirstOrDefault(c => c.ClientId == clientId);

        if (client == null)
        {
            _logger.LogWarning($"Client '{clientId}' not found in registered clients. Available clients: {string.Join(", ", _clients.Select(c => c.ClientId))}");

            // Fallback для публичных клиентов
            if (clientId == "public-client" || clientId == "swagger-ui")
            {
                _logger.LogInformation($"Creating fallback client for '{clientId}'");
                client = new OAuthClient
                {
                    ClientId = clientId,
                    ClientName = $"Fallback {clientId}",
                    RedirectUris = new List<string>
                    {
                        "/swagger/oauth2-redirect.html",
                        "http://localhost:5000/swagger/oauth2-redirect.html",
                        "https://localhost:5001/swagger/oauth2-redirect.html"
                    },
                    Scopes = new List<string> { "openid", "profile", "email", "read", "write" },
                    GrantTypes = new List<string> { "authorization_code" },
                    ResponseTypes = new List<string> { "code", "token" },
                    IsPublic = true
                };
                _clients.Add(client);
            }
        }

        return client;
    }

    public OAuthClient ValidateClient(string? clientId, string? clientSecret)
    {
        if (string.IsNullOrEmpty(clientId))
        {
            throw new Exception("Client ID is required");
        }

        var client = GetClient(clientId);
        if (client == null)
        {
            throw new Exception($"Unknown client: {clientId}");
        }

        if (!client.IsPublic && client.RequiresSecret && client.ClientSecret != clientSecret)
        {
            throw new Exception("Invalid client credentials");
        }

        return client;
    }

    public string GenerateAccessToken(OAuthClient client, string baseUrl)
    {
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, client.ClientId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim("client_id", client.ClientId),
            new Claim("iss", $"{baseUrl}/oauth"),
            new Claim("aud", client.ClientId)
        };

        // Добавляем scopes
        foreach (var scope in client.Scopes)
        {
            claims.Add(new Claim("scope", scope));
        }

        var token = new JwtSecurityToken(
            issuer: $"{baseUrl}/oauth",
            audience: client.ClientId,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: _signingCredentials
        );

        return _tokenHandler.WriteToken(token);
    }

    public string GenerateAccessToken(OAuthClient client, string baseUrl, string username)
    {
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim("client_id", client.ClientId),
            new Claim("username", username),
            new Claim("iss", $"{baseUrl}/oauth"),
            new Claim("aud", client.ClientId)
        };

        foreach (var scope in client.Scopes)
        {
            claims.Add(new Claim("scope", scope));
        }

        var token = new JwtSecurityToken(
            issuer: $"{baseUrl}/oauth",
            audience: client.ClientId,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: _signingCredentials
        );

        return _tokenHandler.WriteToken(token);
    }

    public OAuthWellKnownConfiguration GetWellKnownConfiguration(string baseUrl)
    {
        _issuer = $"{baseUrl}/oauth";

        return new OAuthWellKnownConfiguration
        {
            Issuer = _issuer,
            AuthorizationEndpoint = $"{baseUrl}/oauth/authorize",
            TokenEndpoint = $"{baseUrl}/oauth/token",
            UserinfoEndpoint = $"{baseUrl}/oauth/userinfo",
            JwksUri = $"{baseUrl}/oauth/jwks",
            RegistrationEndpoint = $"{baseUrl}/oauth/register",
            ScopesSupported = new[] { "openid", "profile", "email", "read", "write" },
            ResponseTypesSupported = new[] { "code", "token", "id_token", "code id_token", "code token" },
            GrantTypesSupported = new[] { "authorization_code", "refresh_token", "client_credentials", "password" },
            SubjectTypesSupported = new[] { "public" },
            IdTokenSigningAlgValuesSupported = new[] { "RS256" },
            ClaimsSupported = new[] { "sub", "name", "given_name", "family_name", "email", "email_verified", "preferred_username", "picture" }
        };
    }

    public OAuthJwks GetJwks()
    {
        var parameters = _rsa.ExportParameters(false);

        return new OAuthJwks
        {
            Keys = new List<OAuthJwk>
            {
                new OAuthJwk
                {
                    Kty = "RSA",
                    Kid = "mock-key-1",
                    Use = "sig",
                    Alg = "RS256",
                    N = Convert.ToBase64String(parameters.Modulus!),
                    E = Convert.ToBase64String(parameters.Exponent!)
                }
            }
        };
    }

    public OAuthTokenResponse ProcessTokenRequest(OAuthTokenRequest request, string baseUrl)
    {
        try
        {
            _logger.LogInformation($"Processing token request: grant_type={request.GrantType}, client_id={request.ClientId}");

            switch (request.GrantType)
            {
                case "authorization_code":
                    return HandleAuthorizationCodeGrant(request, baseUrl);
                case "refresh_token":
                    return HandleRefreshTokenGrant(request, baseUrl);
                case "client_credentials":
                    return HandleClientCredentialsGrant(request, baseUrl);
                case "password":
                    return HandlePasswordGrant(request, baseUrl);
                default:
                    throw new Exception($"Unsupported grant type: {request.GrantType}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing token request");
            throw;
        }
    }

    private OAuthTokenResponse HandleAuthorizationCodeGrant(OAuthTokenRequest request, string baseUrl)
    {
        var client = ValidateClient(request.ClientId, request.ClientSecret);

        if (string.IsNullOrEmpty(request.Code) || !_authorizationCodes.TryGetValue(request.Code, out var clientId))
        {
            throw new Exception("Invalid authorization code");
        }

        if (clientId != request.ClientId)
        {
            throw new Exception("Authorization code does not match client");
        }

        _authorizationCodes.Remove(request.Code);

        var accessToken = GenerateAccessToken(client, baseUrl);
        var refreshToken = GenerateRefreshToken(client.ClientId);
        var idToken = GenerateIdToken(client, baseUrl);

        return new OAuthTokenResponse
        {
            AccessToken = accessToken,
            TokenType = "Bearer",
            ExpiresIn = 3600,
            RefreshToken = refreshToken,
            IdToken = idToken,
            Scope = string.Join(" ", request.Scope?.Split(' ') ?? client.Scopes.ToArray())
        };
    }

    private OAuthTokenResponse HandleRefreshTokenGrant(OAuthTokenRequest request, string baseUrl)
    {
        var client = ValidateClient(request.ClientId, request.ClientSecret);

        if (string.IsNullOrEmpty(request.RefreshToken) || !_refreshTokens.TryGetValue(request.RefreshToken, out var storedClientId))
        {
            throw new Exception("Invalid refresh token");
        }

        if (storedClientId != request.ClientId)
        {
            throw new Exception("Refresh token does not match client");
        }

        var accessToken = GenerateAccessToken(client, baseUrl);
        var newRefreshToken = GenerateRefreshToken(client.ClientId);

        _refreshTokens.Remove(request.RefreshToken);

        return new OAuthTokenResponse
        {
            AccessToken = accessToken,
            TokenType = "Bearer",
            ExpiresIn = 3600,
            RefreshToken = newRefreshToken,
            Scope = request.Scope ?? string.Join(" ", client.Scopes)
        };
    }

    private OAuthTokenResponse HandleClientCredentialsGrant(OAuthTokenRequest request, string baseUrl)
    {
        var client = ValidateClient(request.ClientId, request.ClientSecret);

        if (!client.GrantTypes.Contains("client_credentials"))
        {
            throw new Exception("Client credentials grant not allowed for this client");
        }

        var accessToken = GenerateAccessToken(client, baseUrl);

        return new OAuthTokenResponse
        {
            AccessToken = accessToken,
            TokenType = "Bearer",
            ExpiresIn = 3600,
            Scope = string.Join(" ", request.Scope?.Split(' ') ?? client.Scopes.ToArray())
        };
    }

    private OAuthTokenResponse HandlePasswordGrant(OAuthTokenRequest request, string baseUrl)
    {
        var client = ValidateClient(request.ClientId, request.ClientSecret);

        if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            throw new Exception("Username and password are required");
        }

        _logger.LogInformation($"User login attempt: {request.Username}");

        var accessToken = GenerateAccessToken(client, baseUrl, request.Username);
        var refreshToken = GenerateRefreshToken(client.ClientId);
        var idToken = GenerateIdToken(client, baseUrl, request.Username);

        return new OAuthTokenResponse
        {
            AccessToken = accessToken,
            TokenType = "Bearer",
            ExpiresIn = 3600,
            RefreshToken = refreshToken,
            IdToken = idToken,
            Scope = string.Join(" ", request.Scope?.Split(' ') ?? client.Scopes.ToArray())
        };
    }

    public string CreateAuthorizationCode(string clientId, string? redirectUri, string? scope, string? state)
    {
        var code = Guid.NewGuid().ToString("N");
        _authorizationCodes[code] = clientId;
        _logger.LogInformation($"Authorization code created: {code} for client {clientId}");
        return code;
    }

    private string GenerateRefreshToken(string clientId)
    {
        var token = Guid.NewGuid().ToString("N");
        _refreshTokens[token] = clientId;
        return token;
    }

    private string GenerateIdToken(OAuthClient client, string baseUrl, string? username = null)
    {
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, username ?? client.ClientId),
            new Claim(JwtRegisteredClaimNames.Iss, _issuer),
            new Claim(JwtRegisteredClaimNames.Aud, client.ClientId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim("name", username ?? "Mock User"),
            new Claim("given_name", "Mock"),
            new Claim("family_name", "User"),
            new Claim("email", $"{username ?? client.ClientId}@example.com"),
            new Claim("email_verified", "true"),
            new Claim("preferred_username", username ?? client.ClientId)
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: client.ClientId,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: _signingCredentials
        );

        return _tokenHandler.WriteToken(token);
    }

    public OAuthUserInfo GetUserInfo(string? accessToken)
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            throw new Exception("Access token is required");
        }

        try
        {
            var token = _tokenHandler.ReadJwtToken(accessToken);
            var sub = token.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;

            return new OAuthUserInfo
            {
                Sub = sub,
                Name = token.Claims.FirstOrDefault(c => c.Type == "name")?.Value ?? "Mock User",
                GivenName = token.Claims.FirstOrDefault(c => c.Type == "given_name")?.Value ?? "Mock",
                FamilyName = token.Claims.FirstOrDefault(c => c.Type == "family_name")?.Value ?? "User",
                Email = token.Claims.FirstOrDefault(c => c.Type == "email")?.Value ?? $"{sub}@example.com",
                EmailVerified = true,
                PreferredUsername = sub,
                Picture = $"https://ui-avatars.com/api/?background=random&name={Uri.EscapeDataString(sub ?? "Mock+User")}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating access token");
            throw new Exception("Invalid access token");
        }
    }

    public OAuthRegistrationResponse RegisterClient(OAuthRegistrationRequest request)
    {
        var clientId = Guid.NewGuid().ToString("N");
        var clientSecret = Guid.NewGuid().ToString("N");

        var client = new OAuthClient
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            ClientName = request.ClientName,
            RedirectUris = request.RedirectUris ?? new List<string>(),
            Scopes = request.Scope?.Split(' ')?.ToList() ?? new List<string> { "openid", "profile", "email" },
            GrantTypes = request.GrantTypes ?? new List<string> { "authorization_code", "refresh_token" },
            ResponseTypes = request.ResponseTypes ?? new List<string> { "code" },
            Contacts = request.Contacts,
            TosUri = request.TosUri,
            PolicyUri = request.PolicyUri,
            LogoUri = request.LogoUri,
            CreatedAt = DateTime.UtcNow,
            IsPublic = string.IsNullOrEmpty(clientSecret)
        };

        _clients.Add(client);

        return new OAuthRegistrationResponse
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            ClientIdIssuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ClientSecretExpiresAt = 0,
            ClientName = request.ClientName,
            RedirectUris = request.RedirectUris,
            GrantTypes = request.GrantTypes,
            ResponseTypes = request.ResponseTypes,
            Scope = request.Scope,
            Contacts = request.Contacts,
            TosUri = request.TosUri,
            PolicyUri = request.PolicyUri,
            LogoUri = request.LogoUri
        };
    }
}