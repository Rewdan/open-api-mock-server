
namespace OpenApiMockServer.WebApi.Services;

public class AuthService
{
    private readonly ILogger<AuthService> _logger;
    private readonly Dictionary<string, string> _validApiKeys = new();
    private readonly HashSet<string> _validTokens = new();
    private readonly OAuthService _oauthService;
    public AuthService(ILogger<AuthService> logger, OAuthService oauthService)
    {

        _logger = logger;

        // API Keys
        _validApiKeys["test-api-key"] = "test-api-key";
        _validApiKeys["special-key"] = "special-key";

        // Bearer Tokens
        _validTokens.Add("valid-token-123");
        _validTokens.Add("test-token-456");
        _validTokens.Add("mock-token-789");
        _oauthService = oauthService;
    }
    private bool ValidateApiKey(HttpContext context, out string? errorMessage)
    {
        errorMessage = null;

        _logger.LogDebug("Validating API Key");

        // Проверяем в заголовках
        if (context.Request.Headers.TryGetValue("api_key", out var apiKeyHeader) ||
            context.Request.Headers.TryGetValue("X-API-Key", out apiKeyHeader) ||
            context.Request.Headers.TryGetValue("Api-Key", out apiKeyHeader))
        {
            var apiKey = apiKeyHeader.ToString();
            _logger.LogDebug($"API Key from header: {apiKey}");

            if (_validApiKeys.ContainsKey(apiKey) || _validApiKeys.ContainsValue(apiKey))
            {
                _logger.LogDebug($"Valid API Key: {apiKey}");
                return true;
            }
        }

        // Проверяем в query параметрах
        if (context.Request.Query.TryGetValue("api_key", out var apiKeyQuery) ||
            context.Request.Query.TryGetValue("apikey", out apiKeyQuery))
        {
            var apiKey = apiKeyQuery.ToString();
            _logger.LogDebug($"API Key from query: {apiKey}");

            if (_validApiKeys.ContainsKey(apiKey) || _validApiKeys.ContainsValue(apiKey))
            {
                _logger.LogDebug($"Valid API Key in query: {apiKey}");
                return true;
            }
        }

        errorMessage = "Invalid or missing API Key";
        _logger.LogWarning(errorMessage);
        return false;
    }
    public bool ValidateRequest(HttpContext context, out string? errorMessage, List<string>? requiredSchemes = null)
    {
        errorMessage = null;

        _logger.LogDebug($"Validating request. Required schemes: {string.Join(", ", requiredSchemes ?? new List<string>())}");

        // Если не указаны требуемые схемы, проверяем любую авторизацию
        if (requiredSchemes == null || !requiredSchemes.Any())
        {
            _logger.LogDebug("No specific schemes required, checking any auth");
            return ValidateAnyAuth(context, out errorMessage);
        }

        // Проверяем каждую требуемую схему
        foreach (var scheme in requiredSchemes)
        {
            _logger.LogDebug($"Checking scheme: {scheme}");

            if (scheme.ToLower() == "api_key" || scheme.ToLower() == "apikey")
            {
                if (ValidateApiKey(context, out errorMessage))
                {
                    _logger.LogDebug($"Scheme {scheme} validation succeeded");
                    return true;
                }
            }
            else if (scheme.ToLower() == "bearer" || scheme.ToLower() == "http")
            {
                if (ValidateBearerToken(context, out errorMessage))
                {
                    _logger.LogDebug($"Scheme {scheme} validation succeeded");
                    return true;
                }
            }
            else if (scheme.ToLower() == "basic")
            {
                if (ValidateBasicAuth(context, out errorMessage))
                {
                    _logger.LogDebug($"Scheme {scheme} validation succeeded");
                    return true;
                }
            }
            else if (scheme.ToLower() == "oauth2")
            {
                if (ValidateBearerToken(context, out errorMessage))
                {
                    _logger.LogDebug($"Scheme {scheme} validation succeeded");
                    return true;
                }
            }
            else
            {
                _logger.LogDebug($"Unknown scheme type: {scheme}");
            }
        }

        errorMessage = $"None of the required authentication schemes matched. Required: {string.Join(", ", requiredSchemes)}";
        _logger.LogWarning(errorMessage);
        return false;
    }
    
    private bool ValidateAnyAuth(HttpContext context, out string? errorMessage)
    {
        // Проверяем Bearer Token
        if (ValidateBearerToken(context, out errorMessage))
        {
            return true;
        }

        // Проверяем API Key
        if (ValidateApiKey(context, out errorMessage))
        {
            return true;
        }

        // Проверяем Basic Auth
        if (ValidateBasicAuth(context, out errorMessage))
        {
            return true;
        }

        errorMessage = "No valid authentication provided";
        return false;
    }

    private bool ValidateScheme(HttpContext context, string schemeName, out string? errorMessage)
    {
        errorMessage = null;

        return schemeName.ToLower() switch
        {
            "bearer" or "http" => ValidateBearerToken(context, out errorMessage),
            "api_key" or "apikey" => ValidateApiKey(context, out errorMessage),
            "basic" => ValidateBasicAuth(context, out errorMessage),
            "oauth2" => ValidateBearerToken(context, out errorMessage), // OAuth2 использует Bearer token
            _ => ValidateAnyAuth(context, out errorMessage)
        };
    }
    public bool ValidateBearerToken(HttpContext context, out string? errorMessage)
    {
        errorMessage = null;

        _logger.LogDebug("Validating Bearer token");

        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            errorMessage = "Authorization header is required";
            _logger.LogDebug("No Authorization header found");
            return false;
        }

        var token = authHeader.ToString();
        _logger.LogDebug($"Authorization header value: {token}");

        if (string.IsNullOrWhiteSpace(token))
        {
            errorMessage = "Authorization token is empty";
            return false;
        }

        if (!token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Invalid authorization format. Expected: Bearer {token}";
            return false;
        }

        var actualToken = token.Substring("Bearer ".Length).Trim();
        _logger.LogDebug($"Extracted token: {actualToken}");

        // Проверяем валидность токена через встроенные токены
        if (_validTokens.Contains(token) || _validTokens.Contains(actualToken))
        {
            _logger.LogDebug($"Valid Bearer token from built-in list: {actualToken}");
            return true;
        }

        // Проверяем через OAuthService
        try
        {
            var userInfo = _oauthService.GetUserInfo(actualToken);
            if (userInfo != null)
            {
                _logger.LogDebug($"Valid OAuth2 token for user: {userInfo.Sub}");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"Token validation via OAuthService failed: {ex.Message}");
        }

        errorMessage = "Invalid or expired token";
        _logger.LogDebug($"Token validation failed for: {actualToken}");
        return false;
    }
  
    private bool ValidateBasicAuth(HttpContext context, out string? errorMessage)
    {
        errorMessage = null;

        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            errorMessage = "Authorization header is required for Basic auth";
            return false;
        }

        var auth = authHeader.ToString();

        if (!auth.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Invalid authorization format. Expected: Basic {credentials}";
            return false;
        }

        var credentials = auth.Substring("Basic ".Length).Trim();

        try
        {
            var decodedBytes = Convert.FromBase64String(credentials);
            var decoded = System.Text.Encoding.UTF8.GetString(decodedBytes);
            var parts = decoded.Split(':', 2);

            if (parts.Length == 2)
            {
                var username = parts[0];
                var password = parts[1];

                // Моковая проверка - принимаем любые учетные данные
                _logger.LogDebug($"Basic auth attempt: {username}");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Invalid Basic auth format: {ex.Message}");
        }

        errorMessage = "Invalid Basic authentication credentials";
        return false;
    }

    public bool IsOpenApiEndpoint(string path)
    {
        return path.EndsWith("/openapi") ||
               path.EndsWith("/swagger-ui") ||
               path == "/" ||
               path == "/swagger" ||
               path.Contains("/oauth/");
    }
}