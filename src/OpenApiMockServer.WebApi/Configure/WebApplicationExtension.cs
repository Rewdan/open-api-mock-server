using Microsoft.AspNetCore.Builder;
using OpenApiMockServer.WebApi.Middleware;
using OpenApiMockServer.WebApi.Models;
using OpenApiMockServer.WebApi.Services;

namespace OpenApiMockServer.WebApi.Configure
{

    public static class WebApplicationExtension
    {
        public static async Task<WebApplication?> ConfigurateMock(this WebApplication app)
        {

            // Используем middleware для обработки ошибок
            app.UseMiddleware<ErrorHandlingMiddleware>();

            // Получаем сервисы
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            var oauthService = app.Services.GetRequiredService<OAuthService>();
            app.UseSwagger();
            app.UseSwaggerUI();
            // Определяем базовый URL для OAuth2
            var urls = app.Configuration["ASPNETCORE_URLS"] ?? "http://localhost:5000";
            var baseUrl = urls.Split(';').FirstOrDefault() ?? "http://localhost:5000";
            baseUrl = baseUrl.TrimEnd('/');

            // Инициализируем OAuth2 клиентов с правильным базовым URL
            logger.LogInformation($"Initializing OAuth2 service with base URL: {baseUrl}");
            oauthService.InitializeClients(baseUrl);

            // Проверяем, что клиенты созданы
            var clients = oauthService.GetAllClients();
            logger.LogInformation($"Available OAuth2 clients: {string.Join(", ", clients.Select(c => $"{c.ClientId}{(c.IsPublic ? "(public)" : "(confidential)")}"))}");

            // Регистрируем OAuth2 эндпоинты
            var oauthEndpointBuilder = app.Services.GetRequiredService<OAuthEndpointBuilder>();
            oauthEndpointBuilder.BuildEndpoints(app);
            logger.LogInformation("OAuth2 endpoints registered");

            // Главная страница со списком сервисов
            app.MapGet("/", (IConfiguration config) =>
            {
                var services = config.GetSection("Services").Get<Dictionary<string, ServiceDefinition>>();

                var serviceList = services?.Select(s => new ServiceDetail(
            s.Key,
            $"/{s.Key}",
            $"/{s.Key}/openapi",
            $"/{s.Key}/swagger-ui",
            "active"
                ));

                return Results.Json(new
                {
                    message = "OpenAPI Mock Server",
                    version = "1.0",
                    services = serviceList ?? Array.Empty<ServiceDetail>(),
                    oauth = new
                    {
                        wellKnown = "/oauth/.well-known/openid-configuration",
                        authorize = "/oauth/authorize",
                        token = "/oauth/token",
                        userinfo = "/oauth/userinfo",
                        jwks = "/oauth/jwks",
                        clients = "/oauth/clients",
                        testClients = new MockOuthData[]
                        { new MockOuthData( "test-client", "test-secret", "confidential" ),
                new MockOuthData { ClientId = "public-client", Type = "public" },
                new MockOuthData { ClientId = "swagger-ui", Type = "public" }
                        }
                    },
                    documentation = new
                    {
                        swaggerUi = "/swagger",
                        serviceSwaggerUi = "/{serviceName}/swagger-ui",
                        serviceOpenApi = "/{serviceName}/openapi"
                    }
                });
            });

            // Загружаем конфигурацию и инициализируем мок-сервисы
            var serviceConfig = app.Configuration.GetSection("Services").Get<Dictionary<string, ServiceDefinition>>();

            if (serviceConfig != null)
            {
                var parser = app.Services.GetRequiredService<OpenApiParser>();
                var endpointBuilder = app.Services.GetRequiredService<MockEndpointBuilder>();

                foreach (var service in serviceConfig)
                {
                    var serviceInfo = new ServiceInfo
                    {
                        Name = service.Key,
                        OpenApiUrl = service.Value.OpenApiUrl,
                        //BasePath = service.Value.BasePath ?? $"/{service.Key}"
                    };

                    try
                    {
                        logger.LogInformation($"Loading OpenAPI for service {service.Key} from {service.Value.OpenApiUrl}");

                        var document = await parser.ParseOpenApiUrlAsync(service.Value.OpenApiUrl);

                        if (document != null)
                        {
                            serviceInfo.IsConfigured = true;
                            serviceInfo.OpenApiDocument = document;
                            logger.LogInformation($"Service {service.Key} loaded successfully");
                        }
                        else
                        {
                            logger.LogWarning($"Service {service.Key} returned null document");
                        }
                    }
                    catch (Exception ex)
                    {
                        serviceInfo.IsConfigured = false;
                        serviceInfo.ConfigurationError = ex.Message;
                        logger.LogError(ex, $"Error configuring service {service.Key}");
                    }

                    endpointBuilder.BuildEndpoints(app, serviceInfo);
                }
            }
            else
            {
                logger.LogWarning("No services configured in appsettings.json");
            }


            // Тестовый эндпоинт для проверки OAuth2 токена
            app.MapGet("/test-token", async (HttpContext context) =>
            {
                var authHeader = context.Request.Headers["Authorization"].ToString();
                var token = authHeader.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrEmpty(token))
                {
                    return Results.Json(new
                    {
                        message = "No token provided. Please authorize first.",
                        status = "unauthorized"
                    }, statusCode: 401);
                }

                try
                {
                    var userInfo = oauthService.GetUserInfo(token);
                    return Results.Json(new
                    {
                        message = "Token is valid!",
                        status = "authorized",
                        user = userInfo,
                        token = token.Substring(0, Math.Min(30, token.Length)) + "..."
                    });
                }
                catch (Exception ex)
                {
                    return Results.Json(new
                    {
                        error = ex.Message,
                        status = "invalid_token"
                    }, statusCode: 401);
                }
            });
            // Тестовый эндпоинт для проверки OAuth2
            app.MapGet("/test-oauth", async (HttpContext context) =>
            {
                var authHeader = context.Request.Headers["Authorization"].ToString();
                var token = authHeader.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrEmpty(token))
                {
                    return Results.Json(new { message = "No token provided. Try: Authorization: Bearer valid-token-123" });
                }

                try
                {
                    var userInfo = oauthService.GetUserInfo(token);
                    return Results.Json(new
                    {
                        message = "Token is valid",
                        user = userInfo,
                        token = token.Substring(0, Math.Min(20, token.Length)) + "..."
                    });
                }
                catch (Exception ex)
                {
                    return Results.Json(new { error = ex.Message }, statusCode: 401);
                }
            });
            return app;
        }

    }


}
