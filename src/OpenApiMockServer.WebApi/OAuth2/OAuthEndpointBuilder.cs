using Microsoft.AspNetCore.WebUtilities;
using OpenApiMockServer.WebApi.Models;
using OpenApiMockServer.WebApi.OAuth2;

namespace OpenApiMockServer.WebApi.Services;

public class OAuthEndpointBuilder
{
    private readonly ILogger<OAuthEndpointBuilder> _logger;
    private readonly OAuthService _oauthService;

    public OAuthEndpointBuilder(ILogger<OAuthEndpointBuilder> logger, OAuthService oauthService)
    {
        _logger = logger;
        _oauthService = oauthService;
    }

    public void BuildEndpoints(WebApplication app)
    {
        var basePath = "/oauth";

        // OpenID Connect Discovery endpoint
        app.MapGet($"{basePath}/.well-known/openid-configuration", (HttpContext context) =>
        {
            var baseUrl = GetBaseUrl(context);
            var config = _oauthService.GetWellKnownConfiguration(baseUrl);
            return Results.Json(config);
        });

        // JWKS endpoint
        app.MapGet($"{basePath}/jwks", () =>
        {
            var jwks = _oauthService.GetJwks();
            return Results.Json(jwks);
        });

        // Authorization endpoint
        app.MapGet($"{basePath}/authorize", (HttpContext context) =>
        {
            var baseUrl = GetBaseUrl(context);
            var query = context.Request.Query;
            var clientId = query["client_id"].ToString();
            var redirectUri = query["redirect_uri"].ToString();
            var responseType = query["response_type"].ToString();
            var scope = query["scope"].ToString();
            var state = query["state"].ToString();

            _logger.LogInformation($"Authorization request: client_id={clientId}, redirect_uri={redirectUri}, response_type={responseType}");

            var client = _oauthService.GetClient(clientId);
            if (client == null)
            {
                _logger.LogWarning($"Unknown client: {clientId}");
                return Results.BadRequest(new OAuthError
                {
                    Error = "unauthorized_client",
                    ErrorDescription = $"Unknown client: {clientId}"
                });
            }

            // Проверяем redirect_uri
            var isValidRedirectUri = false;
            var normalizedRedirectUri = redirectUri?.TrimEnd('/', '?', '#');

            foreach (var allowedUri in client.RedirectUris)
            {
                var normalizedAllowedUri = allowedUri.TrimEnd('/', '?', '#');

                if (normalizedRedirectUri == normalizedAllowedUri ||
                    normalizedRedirectUri?.StartsWith(normalizedAllowedUri) == true)
                {
                    isValidRedirectUri = true;
                    break;
                }
            }

            if (!isValidRedirectUri)
            {
                _logger.LogWarning($"Invalid redirect_uri: {redirectUri}. Allowed: {string.Join(", ", client.RedirectUris)}");

                if (!string.IsNullOrEmpty(redirectUri))
                {
                    var errorUrl2 = QueryHelpers.AddQueryString(redirectUri, new Dictionary<string, string?>
                    {
                        ["error"] = "invalid_request",
                        ["error_description"] = "Invalid redirect_uri"
                    });
                    return Results.Redirect(errorUrl2);
                }

                return Results.BadRequest(new OAuthError
                {
                    Error = "invalid_request",
                    ErrorDescription = $"Invalid redirect_uri: {redirectUri}. Allowed: {string.Join(", ", client.RedirectUris)}"
                });
            }

            // Поддержка Implicit Flow (response_type=token)
            if (responseType == "token")
            {
                _logger.LogInformation($"Implicit flow requested for client: {clientId}");

                // Генерируем access token напрямую (без кода)
                var baseUrlForToken = baseUrl;
                var accessToken = _oauthService.GenerateAccessToken(client, baseUrlForToken);
                var expiresIn = 3600;

                // Формируем URL с токеном в fragment (#)
                var redirectUrl = $"{redirectUri}#access_token={accessToken}&token_type=Bearer&expires_in={expiresIn}&scope={scope ?? string.Join(" ", client.Scopes)}";

                if (!string.IsNullOrEmpty(state))
                {
                    redirectUrl += $"&state={state}";
                }

                _logger.LogInformation($"Redirecting to: {redirectUrl}");

                // Возвращаем HTML страницу с редиректом
                var html = GetImplicitFlowRedirectHtml(redirectUrl);
                context.Response.ContentType = "text/html; charset=utf-8";
                return Results.Content(html, "text/html");
            }

            // Authorization Code Flow (response_type=code)
            if (responseType == "code")
            {
                // Создаем код авторизации
                var code = _oauthService.CreateAuthorizationCode(clientId, redirectUri, scope, state);

                // Формируем URL для редиректа с кодом
                var redirectUrl = QueryHelpers.AddQueryString(redirectUri, new Dictionary<string, string?>
                {
                    ["code"] = code,
                    ["state"] = state
                });

                _logger.LogInformation($"Authorization code flow, redirecting to: {redirectUrl}");

                // Возвращаем HTML страницу с формой авторизации
                var html = GetAuthorizationPageHtml(redirectUrl, baseUrl, clientId);
                context.Response.ContentType = "text/html; charset=utf-8";
                return Results.Content(html, "text/html");
            }

            // Неподдерживаемый response_type
            var errorUrl = QueryHelpers.AddQueryString(redirectUri, new Dictionary<string, string?>
            {
                ["error"] = "unsupported_response_type",
                ["error_description"] = $"Unsupported response_type: {responseType}. Supported: code, token",
                ["state"] = state
            });
            return Results.Redirect(errorUrl);
        });
        // Token endpoint
        app.MapPost($"{basePath}/token", async (HttpContext context) =>
        {
            try
            {
                var baseUrl = GetBaseUrl(context);

                var form = await context.Request.ReadFormAsync();
                var clientId = form["client_id"].ToString();
                var clientSecret = form["client_secret"].ToString();

                var client = _oauthService.GetClient(clientId);
                if (client == null)
                {
                    return Results.BadRequest(new OAuthError
                    {
                        Error = "invalid_client",
                        ErrorDescription = $"Unknown client: {clientId}"
                    });
                }

                // Для публичных клиентов client_secret не требуется
                if (!client.IsPublic && client.RequiresSecret && client.ClientSecret != clientSecret)
                {
                    return Results.BadRequest(new OAuthError
                    {
                        Error = "invalid_client",
                        ErrorDescription = "Invalid client credentials"
                    });
                }

                var request = new OAuthTokenRequest
                {
                    GrantType = form["grant_type"],
                    ClientId = clientId,
                    ClientSecret = clientSecret,
                    Username = form["username"],
                    Password = form["password"],
                    RefreshToken = form["refresh_token"],
                    Code = form["code"],
                    RedirectUri = form["redirect_uri"],
                    Scope = form["scope"]
                };

                var response = _oauthService.ProcessTokenRequest(request, baseUrl);

                return Results.Json(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token endpoint error");
                return Results.Json(new OAuthError
                {
                    Error = "invalid_request",
                    ErrorDescription = ex.Message
                }, statusCode: 400);
            }
        });

        // UserInfo endpoint
        app.MapPost($"{basePath}/userinfo", (HttpContext context) =>
        {
            var authHeader = context.Request.Headers["Authorization"].ToString();
            var token = authHeader.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(token))
            {
                return Results.Json(new OAuthError
                {
                    Error = "invalid_token",
                    ErrorDescription = "No token provided"
                }, statusCode: 401);
            }

            try
            {
                var userInfo = _oauthService.GetUserInfo(token);
                return Results.Json(userInfo);
            }
            catch (Exception ex)
            {
                return Results.Json(new OAuthError
                {
                    Error = "invalid_token",
                    ErrorDescription = ex.Message
                }, statusCode: 401);
            }
        });

        // Logout endpoint
        app.MapPost($"{basePath}/logout", (HttpContext context) =>
        {
            return Results.Json(new { message = "Logged out successfully" });
        });

        // Registration endpoint
        app.MapPost($"{basePath}/register", async (HttpContext context) =>
        {
            try
            {
                var request = await context.Request.ReadFromJsonAsync<OAuthRegistrationRequest>();

                if (request == null)
                {
                    return Results.BadRequest(new OAuthError
                    {
                        Error = "invalid_request",
                        ErrorDescription = "Invalid request body"
                    });
                }

                if (string.IsNullOrEmpty(request.ClientName))
                {
                    return Results.BadRequest(new OAuthError
                    {
                        Error = "invalid_client_metadata",
                        ErrorDescription = "client_name is required"
                    });
                }

                if (request.RedirectUris == null || !request.RedirectUris.Any())
                {
                    return Results.BadRequest(new OAuthError
                    {
                        Error = "invalid_redirect_uri",
                        ErrorDescription = "At least one redirect_uri is required"
                    });
                }

                var response = _oauthService.RegisterClient(request);

                return Results.Json(response, statusCode: 201);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Registration endpoint error");
                return Results.BadRequest(new OAuthError
                {
                    Error = "server_error",
                    ErrorDescription = ex.Message
                });
            }
        });

        // Swagger OAuth2 redirect page
        app.MapGet("/swagger/oauth2-redirect.html", async (HttpContext context) =>
        {
            var html = GetSwaggerOAuth2RedirectHtml();
            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync(html);
        });

        _logger.LogInformation("OAuth2/OpenID Connect endpoints registered at /oauth");
    }

    private string GetBaseUrl(HttpContext context)
    {
        return $"{context.Request.Scheme}://{context.Request.Host}";
    }
    private string GetImplicitFlowRedirectHtml(string redirectUrl)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <title>OAuth2 Implicit Flow Redirect</title>
    <meta charset='UTF-8'>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
        }}
        .message {{
            background: white;
            padding: 30px;
            border-radius: 12px;
            text-align: center;
            box-shadow: 0 10px 30px rgba(0,0,0,0.2);
            animation: fadeIn 0.5s ease-out;
        }}
        @keyframes fadeIn {{
            from {{ opacity: 0; transform: scale(0.9); }}
            to {{ opacity: 1; transform: scale(1); }}
        }}
        .spinner {{
            display: inline-block;
            width: 40px;
            height: 40px;
            border: 4px solid #f3f3f3;
            border-top: 4px solid #4caf50;
            border-radius: 50%;
            animation: spin 1s linear infinite;
            margin-bottom: 20px;
        }}
        @keyframes spin {{
            0% {{ transform: rotate(0deg); }}
            100% {{ transform: rotate(360deg); }}
        }}
        h3 {{ color: #333; margin: 0 0 10px 0; }}
        p {{ color: #666; margin: 0; }}
    </style>
</head>
<body>
    <div class='message'>
        <div class='spinner'></div>
        <h3>✅ Authorization Successful</h3>
        <p>Redirecting back to application...</p>
    </div>
    
    <script>
        // Перенаправляем на URL с токеном
        window.location.href = '{redirectUrl}';
    </script>
</body>
</html>";
    }
    private string GetAuthorizationPageHtml(string redirectUrl, string baseUrl, string clientId)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <title>Mock OAuth2 Authorization</title>
    <meta charset='UTF-8'>
    <style>
        * {{
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }}
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            min-height: 100vh;
            display: flex;
            justify-content: center;
            align-items: center;
            padding: 20px;
        }}
        .card {{
            background: white;
            border-radius: 16px;
            padding: 32px;
            max-width: 500px;
            width: 100%;
            box-shadow: 0 20px 60px rgba(0,0,0,0.3);
            animation: fadeIn 0.5s ease-out;
        }}
        @keyframes fadeIn {{
            from {{
                opacity: 0;
                transform: translateY(-20px);
            }}
            to {{
                opacity: 1;
                transform: translateY(0);
            }}
        }}
        h1 {{
            color: #333;
            font-size: 24px;
            margin-bottom: 8px;
            display: flex;
            align-items: center;
            gap: 10px;
        }}
        .subtitle {{
            color: #666;
            font-size: 14px;
            margin-bottom: 24px;
            border-bottom: 1px solid #eee;
            padding-bottom: 16px;
        }}
        .info {{
            background: #e3f2fd;
            border-left: 4px solid #2196f3;
            padding: 16px;
            margin: 20px 0;
            border-radius: 8px;
        }}
        .info h3 {{
            color: #1976d2;
            font-size: 14px;
            margin-bottom: 8px;
        }}
        .info p {{
            color: #555;
            font-size: 14px;
            margin: 5px 0;
        }}
        .client-info {{
            background: #f5f5f5;
            border-radius: 8px;
            padding: 12px;
            margin: 16px 0;
            font-family: monospace;
            font-size: 13px;
        }}
        .scopes {{
            background: #f9f9f9;
            padding: 12px;
            border-radius: 8px;
            margin: 16px 0;
        }}
        .scope-item {{
            display: inline-block;
            background: #e0e0e0;
            padding: 4px 12px;
            border-radius: 20px;
            margin: 4px;
            font-size: 12px;
            font-weight: 500;
            color: #555;
        }}
        .scope-item.important {{
            background: #4caf50;
            color: white;
        }}
        .buttons {{
            display: flex;
            gap: 12px;
            margin-top: 24px;
        }}
        button {{
            flex: 1;
            padding: 12px 24px;
            font-size: 16px;
            font-weight: 600;
            border: none;
            border-radius: 8px;
            cursor: pointer;
            transition: all 0.3s ease;
        }}
        .authorize {{
            background: #4caf50;
            color: white;
        }}
        .authorize:hover {{
            background: #45a049;
            transform: translateY(-2px);
            box-shadow: 0 4px 12px rgba(76,175,80,0.3);
        }}
        .deny {{
            background: #f44336;
            color: white;
        }}
        .deny:hover {{
            background: #da190b;
            transform: translateY(-2px);
            box-shadow: 0 4px 12px rgba(244,67,54,0.3);
        }}
        .warning {{
            background: #fff3e0;
            border-left: 4px solid #ff9800;
            padding: 12px;
            margin: 16px 0;
            border-radius: 8px;
            font-size: 13px;
        }}
        .client-id {{
            background: #263238;
            color: #ffd966;
            padding: 2px 6px;
            border-radius: 4px;
            font-family: monospace;
        }}
    </style>
</head>
<body>
    <div class='card'>
        <h1>
            <span>🔐</span>
            Mock OAuth2 Authorization
        </h1>
        <div class='subtitle'>
            <span>Application wants to access your data</span>
        </div>
        
        <div class='client-info'>
            <strong>Client ID:</strong> <span class='client-id'>{System.Security.SecurityElement.Escape(clientId)}</span>
        </div>
        
        <div class='info'>
            <h3>📋 Requesting permissions:</h3>
            <div class='scopes'>
                <span class='scope-item important'>openid</span>
                <span class='scope-item important'>profile</span>
                <span class='scope-item'>email</span>
                <span class='scope-item'>read</span>
                <span class='scope-item'>write</span>
            </div>
            <p style='margin-top: 8px; font-size: 12px;'>This application will be able to read and modify your data.</p>
        </div>
        
        <div class='warning'>
            <strong>⚠️ Mock Authorization Server</strong><br>
            This is a mock OAuth2 server for testing purposes. All credentials are accepted.
        </div>
        
        <div class='buttons'>
            <button class='authorize' onclick='authorize()'>✅ Authorize Application</button>
            <button class='deny' onclick='deny()'>❌ Deny</button>
        </div>
    </div>
    
    <script>
        function authorize() {{
            console.log('Authorizing, redirecting to:', '{redirectUrl}');
            window.location.href = '{redirectUrl}';
        }}
        
        function deny() {{
            const errorUrl = new URL('{redirectUrl}');
            errorUrl.searchParams.set('error', 'access_denied');
            errorUrl.searchParams.set('error_description', 'User denied the request');
            console.log('Denying, redirecting to:', errorUrl.toString());
            window.location.href = errorUrl.toString();
        }}
    </script>
</body>
</html>";
    }

    private string GetSwaggerOAuth2RedirectHtml()
    {
        return @"
<!DOCTYPE html>
<html>
<head>
    <title>Swagger UI OAuth2 Redirect</title>
    <meta charset='UTF-8'>
    <style>
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
        }
        .message {
            background: white;
            padding: 30px;
            border-radius: 12px;
            text-align: center;
            box-shadow: 0 10px 30px rgba(0,0,0,0.2);
            animation: fadeIn 0.5s ease-out;
            max-width: 500px;
        }
        @keyframes fadeIn {
            from { opacity: 0; transform: scale(0.9); }
            to { opacity: 1; transform: scale(1); }
        }
        .spinner {
            display: inline-block;
            width: 40px;
            height: 40px;
            border: 4px solid #f3f3f3;
            border-top: 4px solid #4caf50;
            border-radius: 50%;
            animation: spin 1s linear infinite;
            margin-bottom: 20px;
        }
        @keyframes spin {
            0% { transform: rotate(0deg); }
            100% { transform: rotate(360deg); }
        }
        h3 { color: #333; margin: 0 0 10px 0; }
        p { color: #666; margin: 0; }
        .token-info {
            background: #f5f5f5;
            padding: 10px;
            border-radius: 8px;
            font-family: monospace;
            font-size: 11px;
            word-break: break-all;
            margin: 15px 0;
            text-align: left;
            display: none;
        }
        .success { color: #4caf50; }
        .error { color: #f44336; }
    </style>
</head>
<body>
    <div class='message'>
        <div class='spinner'></div>
        <h3 id='status'>✅ Authorization Completed</h3>
        <p id='message'>Processing token...</p>
        <div class='token-info' id='tokenInfo'></div>
    </div>
    
    <script>
        function getParams() {
            var params = {};
            
            // Проверяем hash (для implicit flow)
            if (window.location.hash) {
                var hash = window.location.hash.substring(1);
                if (hash) {
                    hash.split('&').forEach(function(part) {
                        var item = part.split('=');
                        if (item.length === 2) {
                            params[item[0]] = decodeURIComponent(item[1]);
                        }
                    });
                }
            }
            
            // Проверяем query (для authorization code flow)
            if (window.location.search) {
                var query = window.location.search.substring(1);
                if (query) {
                    query.split('&').forEach(function(part) {
                        var item = part.split('=');
                        if (item.length === 2) {
                            params[item[0]] = decodeURIComponent(item[1]);
                        }
                    });
                }
            }
            
            return params;
        }
        
        var params = getParams();
        console.log('OAuth2 redirect received params:', params);
        
        var tokenInfo = document.getElementById('tokenInfo');
        var statusEl = document.getElementById('status');
        var messageEl = document.getElementById('message');
        
        if (params.access_token) {
            tokenInfo.style.display = 'block';
            tokenInfo.innerHTML = '<strong>✓ Access Token:</strong><br>' + params.access_token.substring(0, 50) + '...<br><br>' +
                                  '<strong>Token Type:</strong> ' + (params.token_type || 'Bearer') + '<br>' +
                                  '<strong>Expires In:</strong> ' + (params.expires_in || '3600') + ' seconds';
            statusEl.innerHTML = '✅ Authorization Successful';
            messageEl.innerHTML = 'Token received. Redirecting back to Swagger UI...';
            messageEl.className = 'success';
        } else if (params.error) {
            statusEl.innerHTML = '❌ Authorization Failed';
            messageEl.innerHTML = params.error_description || params.error;
            messageEl.className = 'error';
            tokenInfo.style.display = 'block';
            tokenInfo.innerHTML = '<strong>Error:</strong><br>' + (params.error_description || params.error);
        }
        
        // Отправляем сообщение в Swagger UI
        if (window.opener) {
            console.log('Sending message to opener with params:', params);
            
            var message = {
                type: 'oauth2',
                params: params
            };
            
            // Отправляем сообщение
            window.opener.postMessage(message, window.location.origin);
            
            // Также отправляем в формате для Swagger UI
            if (params.access_token) {
                window.opener.postMessage({
                    swagger: {
                        auth: {
                            token: params.access_token,
                            tokenType: params.token_type || 'Bearer'
                        }
                    }
                }, window.location.origin);
            }
            
            // Закрываем окно через 2 секунды
            setTimeout(function() {
                window.close();
            }, 2000);
        } else {
            console.log('No opener found');
            messageEl.innerHTML = 'No opener window found. Please close this window and check Swagger UI.';
        }
    </script>
</body>
</html>";
    }
}