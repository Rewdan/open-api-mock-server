using Microsoft.OpenApi.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OpenApiMockServer.WebApi.Models;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Extensions;

namespace OpenApiMockServer.WebApi.Services;

public class MockEndpointBuilder
{
    private readonly ILogger<MockEndpointBuilder> _logger;
    private readonly MockDataGenerator _dataGenerator;
    private readonly RequestValidator _requestValidator;
    private readonly AuthService _authService;
    private readonly AuthAnalyzer _authAnalyzer;
    private readonly RelationshipResolver _relationshipResolver;
    private readonly OAuthService _oauthService;
    private readonly HashSet<string> _registeredEndpoints = new();

    public MockEndpointBuilder(
        ILogger<MockEndpointBuilder> logger,
        MockDataGenerator dataGenerator,
        RequestValidator requestValidator,
        AuthService authService,
        AuthAnalyzer authAnalyzer,
        RelationshipResolver relationshipResolver,
        OAuthService oauthService)
    {
        _logger = logger;
        _dataGenerator = dataGenerator;
        _requestValidator = requestValidator;
        _authService = authService;
        _authAnalyzer = authAnalyzer;
        _relationshipResolver = relationshipResolver;
        _oauthService = oauthService;
    }

    public void BuildEndpoints(WebApplication app, ServiceInfo serviceInfo)
    {
        var basePath = $"/{serviceInfo.Name}";

        _registeredEndpoints.Clear();

        if (serviceInfo.OpenApiDocument != null)
        {
            serviceInfo.AuthRequirements = _authAnalyzer.Analyze(serviceInfo.OpenApiDocument);
            _logger.LogInformation($"Service {serviceInfo.Name} - {_authAnalyzer.GetAuthDescription(serviceInfo.AuthRequirements)}");

            var hasOAuth = serviceInfo.AuthRequirements.Schemes.Any(s => s.Type?.ToLower() == "oauth2");
            if (hasOAuth)
            {
                _logger.LogInformation($"Service {serviceInfo.Name} uses OAuth2, mock OAuth2 server will be used");
            }
        }

        RegisterSpecialEndpoints(app, serviceInfo, basePath);

        if (serviceInfo.OpenApiDocument != null)
        {
            RegisterOpenApiEndpoints(app, serviceInfo, basePath);
        }

        _logger.LogInformation($"Mock service created for {serviceInfo.Name}");
    }

    #region Special Endpoints

    private void RegisterSpecialEndpoints(WebApplication app, ServiceInfo serviceInfo, string basePath)
    {
        // OpenAPI JSON endpoint
        var openApiPath = $"{basePath}/openapi";
        if (!_registeredEndpoints.Contains(openApiPath))
        {
            app.MapGet(openApiPath, (HttpContext context) =>
            {
                var document = serviceInfo.OpenApiDocument;
                if (document == null)
                {
                    return Results.Json(new { error = "OpenAPI document not available" }, statusCode: 500);
                }

                var modifiedDocument = CreateMockOpenApiDocument(serviceInfo, basePath, context);
                var json = modifiedDocument.SerializeAsJson(Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0);
                return Results.Content(json, "application/json", Encoding.UTF8);
            });
            _registeredEndpoints.Add(openApiPath);
        }

        // Swagger UI endpoint
        var swaggerUiPath = $"{basePath}/swagger-ui";
        if (!_registeredEndpoints.Contains(swaggerUiPath))
        {
            app.MapGet(swaggerUiPath, async (HttpContext context) =>
            {
                var html = GenerateSwaggerUiHtml(serviceInfo, context);
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(html, Encoding.UTF8);
            });
            _registeredEndpoints.Add(swaggerUiPath);
        }

        // Status endpoint
        var statusPath = $"{basePath}/_status";
        if (!_registeredEndpoints.Contains(statusPath))
        {
            app.MapGet(statusPath, (HttpContext context) =>
            {
                var baseUrl = GetBaseUrl(context);
                var hasOAuth = serviceInfo.AuthRequirements.Schemes.Any(s => s.Type?.ToLower() == "oauth2");

                return Results.Json(new
                {
                    service = serviceInfo.Name,
                    status = "healthy",
                    hasOpenApi = serviceInfo.OpenApiDocument != null,
                    baseUrl = baseUrl,
                    auth = new
                    {
                        requiresAuth = serviceInfo.AuthRequirements.RequiresAuth,
                        description = _authAnalyzer.GetAuthDescription(serviceInfo.AuthRequirements),
                        hasOAuth = hasOAuth
                    }
                });
            });
            _registeredEndpoints.Add(statusPath);
        }

        // Root endpoint
        if (!_registeredEndpoints.Contains(basePath))
        {
            app.MapGet(basePath, (HttpContext context) =>
            {
                var baseUrl = GetBaseUrl(context);
                var hasOAuth = serviceInfo.AuthRequirements.Schemes.Any(s => s.Type?.ToLower() == "oauth2");

                return Results.Json(new
                {
                    service = serviceInfo.Name,
                    status = "active",
                    type = "mock",
                    mockServerUrl = $"{baseUrl}{basePath}",
                    openApiUrl = $"{basePath}/openapi",
                    swaggerUi = $"{basePath}/swagger-ui",
                    auth = new
                    {
                        requiresAuth = serviceInfo.AuthRequirements.RequiresAuth,
                        description = _authAnalyzer.GetAuthDescription(serviceInfo.AuthRequirements),
                        testTokens = serviceInfo.AuthRequirements.RequiresAuth && !hasOAuth ?
                            new[] { "valid-token-123", "test-token-456", "mock-token-789" } : null,
                        oauth2 = hasOAuth ? new
                        {
                            clientId = "public-client",
                            authorizeUrl = $"{baseUrl}/oauth/authorize",
                            tokenUrl = $"{baseUrl}/oauth/token"
                        } : null
                    }
                });
            });
            _registeredEndpoints.Add(basePath);
        }
    }

    #endregion

    #region OpenAPI Endpoints Registration

    private void RegisterOpenApiEndpoints(WebApplication app, ServiceInfo serviceInfo, string basePath)
    {
        if (serviceInfo.OpenApiDocument?.Paths == null) return;

        foreach (var path in serviceInfo.OpenApiDocument.Paths)
        {
            foreach (var operation in path.Value.Operations)
            {
                var originalPath = path.Key;
                var fullPath = $"{basePath}{originalPath}".Replace("//", "/");

                if (_registeredEndpoints.Contains(string.Concat(operation.Key, fullPath))) continue;

                var entityName = ExtractEntityName(operation.Value, originalPath);
                var pathParameters = ExtractPathParameters(operation.Value, originalPath);

                var requiredSchemes = _authAnalyzer.GetRequiredAuthSchemesForPath(
                    originalPath,
                    operation.Key.ToString(),
                    serviceInfo.AuthRequirements);

                _logger.LogInformation($"Registering {operation.Key} {fullPath}");
                _logger.LogInformation($"  Entity: {entityName}");
                _logger.LogInformation($"  Required schemes: {string.Join(", ", requiredSchemes)}");
                _logger.LogInformation($"  Has request body: {operation.Value.RequestBody != null}");
                _logger.LogInformation($"  Response codes: {string.Join(", ", operation.Value.Responses.Keys)}");

                RegisterOperation(app, serviceInfo, fullPath, entityName, operation.Key, operation.Value, pathParameters, requiredSchemes);
                _registeredEndpoints.Add(string.Concat(operation.Key,fullPath));
            }
        }
    }

    private void RegisterOperation(
        WebApplication app,
        ServiceInfo serviceInfo,
        string path,
        string entityName,
        OperationType operationType,
        OpenApiOperation operation,
        List<PathParameter> pathParameters,
        List<string> requiredSchemes)
    {
        switch (operationType)
        {
            case OperationType.Get:
                RegisterGetEndpoint(app, serviceInfo, path, entityName, operation, pathParameters, requiredSchemes);
                break;
            case OperationType.Post:
                RegisterPostEndpoint(app, serviceInfo, path, entityName, operation, requiredSchemes);
                break;
            case OperationType.Put:
            case OperationType.Patch:
                RegisterPutEndpoint(app, serviceInfo, path, entityName, operation, pathParameters, requiredSchemes);
                break;
            case OperationType.Delete:
                RegisterDeleteEndpoint(app, serviceInfo, path, entityName, operation, pathParameters, requiredSchemes);
                break;
        }
    }

    #endregion

    #region HTTP Method Handlers

    private void RegisterGetEndpoint(
        WebApplication app,
        ServiceInfo serviceInfo,
        string path,
        string entityName,
        OpenApiOperation operation,
        List<PathParameter> pathParameters,
        List<string> requiredSchemes)
    {
        var responseSchema = FindResponseSchema(serviceInfo, operation);
        var hasPathParams = pathParameters.Any();

        if (hasPathParams)
        {
            app.MapGet(path, async (HttpContext context) =>
            {
                _logger.LogInformation($"GET request received: {path}");

                var authResult = await ValidateAuth(context, requiredSchemes, serviceInfo);
                if (authResult != null) return authResult;

                var parameters = ExtractParametersFromRoute(context, pathParameters);
                var paramLog = string.Join(", ", parameters.Select(p => $"{p.Key}={p.Value}"));
                _logger.LogInformation($"GET {path} with params: {paramLog}");

                if (responseSchema != null)
                {
                    var response = _dataGenerator.GenerateObject(responseSchema, entityName, serviceInfo.OpenApiDocument);
                    return Results.Ok(response);
                }

                return Results.NotFound();
            });
        }
        else
        {
            app.MapGet(path, async (HttpContext context) =>
            {
                _logger.LogInformation($"GET collection request: {path}");

                var authResult = await ValidateAuth(context, requiredSchemes, serviceInfo);
                if (authResult != null) return authResult;

                if (responseSchema != null)
                {
                    var array = _dataGenerator.GenerateArray(responseSchema, entityName, serviceInfo.OpenApiDocument);
                    return Results.Ok(array);
                }

                return Results.Ok(new JsonArray());
            });
        }
    }
    private void RegisterPostEndpoint(
        WebApplication app,
        ServiceInfo serviceInfo,
        string path,
        string entityName,
        OpenApiOperation operation,
        List<string> requiredSchemes)
    {
        _logger.LogInformation($"Registering POST endpoint: {path}");

        // Проверяем, есть ли multipart/form-data в requestBody
        var hasMultipart = operation.RequestBody?.Content?.ContainsKey("multipart/form-data") == true;

        if (hasMultipart)
        {
            // Обработка multipart/form-data
            app.MapPost(path, async (HttpContext context) =>
            {
                try
                {
                    _logger.LogInformation($"POST multipart request received: {path}");

                    // Проверка авторизации
                    var authResult = await ValidateAuth(context, requiredSchemes, serviceInfo);
                    if (authResult != null) return authResult;

                    // Парсим multipart/form-data
                    var form = await context.Request.ReadFormAsync();
                    var requestBody = new JsonObject();

                    // Получаем схему для multipart
                    var multipartSchema = operation.RequestBody?.Content["multipart/form-data"]?.Schema;

                    if (multipartSchema?.Properties != null)
                    {
                        foreach (var prop in multipartSchema.Properties)
                        {
                            var propName = prop.Key;
                            var propSchema = prop.Value;

                            if (propSchema.Type == "file")
                            {
                                // Обработка файла
                                if (form.Files.Any(f => f.Name == propName))
                                {
                                    var file = form.Files.First(f => f.Name == propName);
                                    var fileInfo = new JsonObject
                                    {
                                        ["fileName"] = file.FileName,
                                        ["contentType"] = file.ContentType,
                                        ["length"] = file.Length,
                                        ["name"] = file.Name
                                    };
                                    requestBody[propName] = fileInfo;
                                    _logger.LogDebug($"File uploaded: {file.FileName} ({file.Length} bytes)");
                                }
                            }
                            else
                            {
                                // Обработка обычных полей
                                if (form.ContainsKey(propName))
                                {
                                    var value = form[propName].ToString();
                                    var convertedValue = ConvertFormValueToJson(value, propSchema);
                                    if (convertedValue != null)
                                    {
                                        requestBody[propName] = convertedValue;
                                    }
                                }
                            }
                        }
                    }

                    // Добавляем все остальные поля из формы
                    foreach (var key in form.Keys)
                    {
                        if (!requestBody.ContainsKey(key) && !(multipartSchema?.Properties?.ContainsKey(key) == true))
                        {
                            requestBody[key] = JsonValue.Create(form[key].ToString());
                        }
                    }

                    _logger.LogInformation($"POST multipart request body: {requestBody.ToJsonString()}");

                    // Валидация запроса
                    var validationResult = await ValidateMultipartRequest(requestBody, operation, serviceInfo);
                    if (validationResult != null) return validationResult;

                    // Находим схему ответа
                    var responseSchema = FindResponseSchema(serviceInfo, operation);

                    if (responseSchema != null)
                    {
                        var response = _dataGenerator.GenerateObject(responseSchema, entityName, serviceInfo.OpenApiDocument);
                        return Results.Ok(response);
                    }

                    return Results.Ok(requestBody);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing POST multipart request {path}");
                    return Results.StatusCode(500);
                }
            });
        }
        else
        {
            // Стандартная обработка JSON
            app.MapPost(path, async (HttpContext context) =>
            {
                try
                {
                    _logger.LogInformation($"POST JSON request received: {path}");

                    var authResult = await ValidateAuth(context, requiredSchemes, serviceInfo);
                    if (authResult != null) return authResult;

                    var requestBody = await ParseRequestBody(context);
                    if (requestBody == null)
                    {
                        return Results.BadRequest(new { error = "Invalid JSON" });
                    }

                    _logger.LogInformation($"POST request body: {requestBody.ToJsonString()}");

                    var validationResult = await ValidateRequest(requestBody, operation, serviceInfo);
                    if (validationResult != null) return validationResult;

                    var responseSchema = FindResponseSchema(serviceInfo, operation);

                    if (responseSchema != null)
                    {
                        var response = _dataGenerator.GenerateObject(responseSchema, entityName, serviceInfo.OpenApiDocument);
                        return Results.Ok(response);
                    }

                    return Results.Ok(requestBody);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing POST request {path}");
                    return Results.StatusCode(500);
                }
            });
        }
    }

    private async Task<IResult?> ValidateMultipartRequest(JsonObject requestBody, OpenApiOperation operation, ServiceInfo serviceInfo)
    {
        var requestSchema = FindRequestSchema(serviceInfo, operation);
        if (requestSchema == null) return null;

        // Для multipart, схема может быть в multipart/form-data
        if (operation.RequestBody?.Content?.ContainsKey("multipart/form-data") == true)
        {
            requestSchema = operation.RequestBody.Content["multipart/form-data"].Schema;
        }

        if (requestSchema == null) return null;

        var errors = _requestValidator.ValidateRequest(requestBody, requestSchema, serviceInfo.OpenApiDocument);
        if (!errors.Any()) return null;

        return Results.BadRequest(new
        {
            error = "Validation Failed",
            validationErrors = errors,
            timestamp = DateTime.UtcNow
        });
    }

    private JsonNode? ConvertFormValueToJson(string value, OpenApiSchema schema)
    {
        if (string.IsNullOrEmpty(value)) return null;

        return schema.Type switch
        {
            "string" => JsonValue.Create(value),
            "integer" => int.TryParse(value, out var intVal) ? JsonValue.Create(intVal) : JsonValue.Create(value),
            "number" => double.TryParse(value, out var doubleVal) ? JsonValue.Create(doubleVal) : JsonValue.Create(value),
            "boolean" => bool.TryParse(value, out var boolVal) ? JsonValue.Create(boolVal) : JsonValue.Create(value),
            "array" => JsonValue.Create(value.Split(',')),
            _ => JsonValue.Create(value)
        };
    }
    private void RegisterPutEndpoint(
        WebApplication app,
        ServiceInfo serviceInfo,
        string path,
        string entityName,
        OpenApiOperation operation,
        List<PathParameter> pathParameters,
        List<string> requiredSchemes)
    {
        _logger.LogInformation($"Registering PUT endpoint: {path}");

        app.MapPut(path, async (HttpContext context) =>
        {
            try
            {
                _logger.LogInformation($"PUT request received: {path}");

                var authResult = await ValidateAuth(context, requiredSchemes, serviceInfo);
                if (authResult != null) return authResult;

                var parameters = ExtractParametersFromRoute(context, pathParameters);
                var paramLog = string.Join(", ", parameters.Select(p => $"{p.Key}={p.Value}"));
                _logger.LogInformation($"PUT {path} with params: {paramLog}");

                var requestBody = await ParseRequestBody(context);
                if (requestBody == null)
                {
                    return Results.BadRequest(new { error = "Invalid JSON" });
                }

                var validationResult = await ValidateRequest(requestBody, operation, serviceInfo);
                if (validationResult != null) return validationResult;

                var responseSchema = FindResponseSchema(serviceInfo, operation);
                var response = responseSchema != null
                    ? _dataGenerator.GenerateObject(responseSchema, entityName, serviceInfo.OpenApiDocument)
                    : requestBody;

                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing PUT request {path}");
                return Results.StatusCode(500);
            }
        });
    }

    private void RegisterDeleteEndpoint(
        WebApplication app,
        ServiceInfo serviceInfo,
        string path,
        string entityName,
        OpenApiOperation operation,
        List<PathParameter> pathParameters,
        List<string> requiredSchemes)
    {
        _logger.LogInformation($"Registering DELETE endpoint: {path}");
        _logger.LogInformation($"  Path parameters: {string.Join(", ", pathParameters.Select(p => $"{p.Name}:{p.Type}"))}");

        var hasPathParams = pathParameters.Any();

        if (hasPathParams)
        {
            // Создаем шаблон маршрута с параметрами
            var routeTemplate = path;

            app.MapDelete(routeTemplate, async (HttpContext context) =>
            {
                _logger.LogInformation($"DELETE request received: {path}");

                // Проверка авторизации
                var authResult = await ValidateAuth(context, requiredSchemes, serviceInfo);
                if (authResult != null) return authResult;

                // Извлекаем параметры из маршрута
                var parameters = ExtractParametersFromRoute(context, pathParameters);
                var paramLog = string.Join(", ", parameters.Select(p => $"{p.Key}={p.Value}"));
                _logger.LogInformation($"DELETE {path} with params: {paramLog}");

                return Results.NoContent();
            });
        }
        else
        {
            // DELETE без параметров
            app.MapDelete(path, async (HttpContext context) =>
            {
                _logger.LogInformation($"DELETE collection request: {path}");

                var authResult = await ValidateAuth(context, requiredSchemes, serviceInfo);
                if (authResult != null) return authResult;

                return Results.NoContent();
            });
        }
    }

    #endregion

    #region Helper Methods
    private async Task<IResult?> ValidateAuth(HttpContext context, List<string> requiredSchemes, ServiceInfo serviceInfo)
    {
        // Если нет требуемых схем, проверяем, нужно ли вообще авторизоваться
        if (requiredSchemes == null || !requiredSchemes.Any())
        {
            _logger.LogDebug("No auth required for this endpoint");
            return null;
        }

        string headerName = "Authorization";

        if (requiredSchemes.Contains("api_key"))
        {
            headerName = "api_key";
        }

        _logger.LogInformation($"Validating auth for endpoint. Required schemes: {string.Join(", ", requiredSchemes)}");
        _logger.LogInformation($"Authorization header present: {context.Request.Headers.ContainsKey(headerName)}");

        // Проверяем, есть ли токен в заголовке
        var authHeader = context.Request.Headers[headerName].ToString();
        var hasAuthHeader = !string.IsNullOrEmpty(authHeader);

        if (!hasAuthHeader)
        {
            _logger.LogWarning($"No Authorization header found, but endpoint requires {string.Join(", ", requiredSchemes)}");
            return HandleUnauthorized($"Authorization required. Required schemes: {string.Join(", ", requiredSchemes)}", serviceInfo, requiredSchemes, false);
        }

        var hasOAuth = requiredSchemes.Any(s =>
            serviceInfo.AuthRequirements.Schemes.Any(scheme =>
                scheme.Name == s && scheme.Type?.ToLower() == "oauth2"));

        if (hasOAuth)
        {
            _logger.LogInformation($"Authorization header value: {authHeader}");

            if (!_authService.ValidateBearerToken(context, out var authError))
            {
                _logger.LogWarning($"Bearer token validation failed: {authError}");
                return HandleUnauthorized(authError!, serviceInfo, requiredSchemes, true);
            }

            _logger.LogInformation("Bearer token validation succeeded");
            return null;
        }

        // Проверяем другие типы авторизации (API Key, Basic)
        if (!_authService.ValidateRequest(context, out var error, requiredSchemes))
        {
            _logger.LogWarning($"Auth validation failed: {error}");
            return HandleUnauthorized(error!, serviceInfo, requiredSchemes, false);
        }

        _logger.LogInformation("Auth validation succeeded");
        return null;
    }
    private IResult HandleUnauthorized(string message, ServiceInfo serviceInfo, List<string> requiredSchemes, bool isOAuth = false)
    {
        var authDescription = _authAnalyzer.GetAuthDescription(serviceInfo.AuthRequirements);
        var baseUrl = GetBaseUrl();

        return Results.Json(new
        {
            error = "Unauthorized",
            details = message,
            statusCode = 401,
            timestamp = DateTime.UtcNow,
            requiredAuth = requiredSchemes.Any()
                ? $"Required authentication: {string.Join(", ", requiredSchemes)}"
                : authDescription,
            oauth2 = isOAuth ? new
            {
                authorizationUrl = $"{baseUrl}/oauth/authorize",
                tokenUrl = $"{baseUrl}/oauth/token",
                clientId = "public-client"
            } : null
        }, statusCode: 401);
    }

    private async Task<JsonObject?> ParseRequestBody(HttpContext context)
    {
        try
        {
            var contentType = context.Request.ContentType;

            // Обработка multipart/form-data
            if (contentType != null && contentType.Contains("multipart/form-data"))
            {
                var form = await context.Request.ReadFormAsync();
                var result = new JsonObject();

                foreach (var key in form.Keys)
                {
                    if (form.Files.Any(f => f.Name == key))
                    {
                        var file = form.Files.First(f => f.Name == key);
                        var fileInfo = new JsonObject
                        {
                            ["fileName"] = file.FileName,
                            ["contentType"] = file.ContentType,
                            ["length"] = file.Length,
                            ["name"] = file.Name
                        };
                        result[key] = fileInfo;
                    }
                    else
                    {
                        result[key] = JsonValue.Create(form[key].ToString());
                    }
                }

                return result;
            }

            // Стандартная обработка JSON
            using var document = await JsonDocument.ParseAsync(context.Request.Body);
            return JsonObject.Parse(document.RootElement.GetRawText())?.AsObject();
        }
        catch
        {
            return null;
        }
    }

    private async Task<IResult?> ValidateRequest(JsonObject requestBody, OpenApiOperation operation, ServiceInfo serviceInfo)
    {
        var requestSchema = FindRequestSchema(serviceInfo, operation);
        if (requestSchema == null) return null;

        var errors = _requestValidator.ValidateRequest(requestBody, requestSchema, serviceInfo.OpenApiDocument);
        if (!errors.Any()) return null;

        return Results.BadRequest(new
        {
            error = "Validation Failed",
            validationErrors = errors,
            timestamp = DateTime.UtcNow
        });
    }

    private Dictionary<string, object> ExtractParametersFromRoute(HttpContext context, List<PathParameter> pathParameters)
    {
        var result = new Dictionary<string, object>();
        var routeValues = context.Request.RouteValues;

        _logger.LogDebug($"Route values: {string.Join(", ", routeValues.Select(v => $"{v.Key}={v.Value}"))}");

        foreach (var param in pathParameters)
        {
            if (routeValues.TryGetValue(param.Name, out var value))
            {
                var convertedValue = ConvertParameterValue(value, param.Type);
                result[param.Name] = convertedValue;
                _logger.LogDebug($"Parameter {param.Name} = {convertedValue} (type: {param.Type})");
            }
            else
            {
                _logger.LogWarning($"Parameter {param.Name} not found in route values");
            }
        }
        return result;
    }

    private object? ConvertParameterValue(object? value, string targetType)
    {
        if (value == null) return null;
        var stringValue = value.ToString();

        return targetType.ToLower() switch
        {
            "integer" or "int64" or "int32" => long.TryParse(stringValue, out var l) ? l : stringValue,
            "number" or "double" or "float" => double.TryParse(stringValue, out var d) ? d : stringValue,
            "boolean" => bool.TryParse(stringValue, out var b) ? b : stringValue,
            _ => stringValue
        };
    }

    private List<PathParameter> ExtractPathParameters(OpenApiOperation operation, string path)
    {
        var parameters = new List<PathParameter>();

        if (operation.Parameters != null)
        {
            foreach (var param in operation.Parameters)
            {
                if (param.In == ParameterLocation.Path || param.In.ToString() == "path")
                {
                    parameters.Add(new PathParameter
                    {
                        Name = param.Name,
                        Required = param.Required,
                        Type = param.Schema?.Type ?? "string",
                        Format = param.Schema?.Format
                    });
                    _logger.LogDebug($"Path parameter: {param.Name} (type: {param.Schema?.Type ?? "string"})");
                }
            }
        }

        var pathMatches = Regex.Matches(path, @"\{(\w+)\}");
        foreach (Match match in pathMatches)
        {
            var paramName = match.Groups[1].Value;
            if (!parameters.Any(p => p.Name == paramName))
            {
                parameters.Add(new PathParameter
                {
                    Name = paramName,
                    Required = true,
                    Type = "string"
                });
                _logger.LogDebug($"Path parameter from URL: {paramName}");
            }
        }

        return parameters;
    }

    private string ExtractEntityName(OpenApiOperation operation, string path)
    {
        if (operation.Tags?.Any() == true) return operation.Tags.First().Name;

        if (!string.IsNullOrEmpty(operation.OperationId))
        {
            var match = Regex.Match(operation.OperationId, "([a-z]+)$", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value.ToLower();
            return operation.OperationId;
        }

        var pathMatch = Regex.Match(path, @"/([^/{]+)");
        return pathMatch.Success ? pathMatch.Groups[1].Value : "default";
    }

    private OpenApiSchema? FindResponseSchema(ServiceInfo serviceInfo, OpenApiOperation operation)
    {
        var successStatuses = new[] { "200", "201", "202", "204", "405" };

        foreach (var statusCode in successStatuses)
        {
            if (operation.Responses.TryGetValue(statusCode, out var response))
            {
                if (response.Content != null && response.Content.Any())
                {
                    if (response.Content.TryGetValue("application/json", out var mediaType) ||
                        response.Content.TryGetValue("*/*", out mediaType))
                    {
                        if (mediaType?.Schema != null) return mediaType.Schema;
                    }
                }

                if (response.Extensions != null && response.Extensions.TryGetValue("schema", out var schemaExtension))
                {
                    try
                    {
                        var schemaJson = schemaExtension.ToString();
                        var schema = ParseSchemaFromExtension(schemaJson, serviceInfo.OpenApiDocument);
                        if (schema != null) return schema;
                    }
                    catch { }
                }
            }
        }

        if (operation.RequestBody != null && operation.RequestBody.Content != null)
        {
            if (operation.RequestBody.Content.TryGetValue("application/json", out var mediaType) ||
                operation.RequestBody.Content.TryGetValue("*/*", out mediaType))
            {
                if (mediaType?.Schema != null)
                {
                    _logger.LogDebug($"Using request schema as response schema for {operation.OperationId}");
                    return mediaType.Schema;
                }
            }
        }

        return null;
    }

    private OpenApiSchema? ParseSchemaFromExtension(string schemaJson, OpenApiDocument? document)
    {
        try
        {
            using var doc = JsonDocument.Parse(schemaJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("$ref", out var refProperty))
            {
                var refValue = refProperty.GetString();
                if (refValue != null && refValue.StartsWith("#/definitions/"))
                {
                    var schemaName = refValue.Substring("#/definitions/".Length);

                    if (document?.Extensions != null &&
                        document.Extensions.TryGetValue("definitions", out var definitionsExtension))
                    {
                        var definitionsJson = definitionsExtension.ToString();
                        using var definitionsDoc = JsonDocument.Parse(definitionsJson);
                        if (definitionsDoc.RootElement.TryGetProperty(schemaName, out var schemaElement))
                        {
                            return ParseSwagger2Schema(schemaElement, schemaName);
                        }
                    }
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private OpenApiSchema? ParseSwagger2Schema(JsonElement schemaElement, string schemaName)
    {
        if (!schemaElement.TryGetProperty("type", out var typeProperty)) return null;

        var schema = new OpenApiSchema
        {
            Type = typeProperty.GetString(),
            Reference = new OpenApiReference
            {
                Id = schemaName,
                Type = ReferenceType.Schema
            }
        };

        if (schemaElement.TryGetProperty("properties", out var properties))
        {
            schema.Properties = new Dictionary<string, OpenApiSchema>();
            foreach (var prop in properties.EnumerateObject())
            {
                var propSchema = new OpenApiSchema
                {
                    Type = prop.Value.GetProperty("type").GetString()
                };

                if (prop.Value.TryGetProperty("format", out var format))
                {
                    propSchema.Format = format.GetString();
                }

                if (prop.Value.TryGetProperty("enum", out var enumValues))
                {
                    propSchema.Enum = new List<IOpenApiAny>();
                    foreach (var enumValue in enumValues.EnumerateArray())
                    {
                        propSchema.Enum.Add(new OpenApiString(enumValue.GetString()));
                    }
                }

                schema.Properties[prop.Name] = propSchema;
            }
        }

        if (schemaElement.TryGetProperty("required", out var required))
        {
            schema.Required = new HashSet<string>();
            foreach (var req in required.EnumerateArray())
            {
                schema.Required.Add(req.GetString());
            }
        }

        return schema;
    }

 private OpenApiSchema? FindRequestSchema(ServiceInfo serviceInfo, OpenApiOperation operation)
{
    // OpenAPI 3.0 - RequestBody
    if (operation.RequestBody?.Content != null)
    {
        // Проверяем application/json
        if (operation.RequestBody.Content.TryGetValue("application/json", out var jsonMediaType))
        {
            return jsonMediaType?.Schema;
        }
        
        // Проверяем multipart/form-data
        if (operation.RequestBody.Content.TryGetValue("multipart/form-data", out var multipartMediaType))
        {
            return multipartMediaType?.Schema;
        }
        
        // Проверяем */*
        if (operation.RequestBody.Content.TryGetValue("*/*", out var anyMediaType))
        {
            return anyMediaType?.Schema;
        }
        
        // Берем первый попавшийся
        var firstContent = operation.RequestBody.Content.FirstOrDefault();
        return firstContent.Value?.Schema;
    }
    
    // Swagger 2.0 - параметры с in: body
    //if (operation.Parameters != null)
    //{
    //    var bodyParam = operation.Parameters.FirstOrDefault(p => p.In == ParameterLocation.Body || p.In.ToString() == "body");
    //    if (bodyParam?.Schema != null)
    //    {
    //        return bodyParam.Schema;
    //    }
    //}
    
    return null;
}

    private string GetBaseUrl(HttpContext? context = null)
    {
        if (context != null) return $"{context.Request.Scheme}://{context.Request.Host}";

        var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5000";
        return urls.Split(';').FirstOrDefault()?.TrimEnd('/') ?? "http://localhost:5000";
    }

    private OpenApiDocument CreateMockOpenApiDocument(ServiceInfo serviceInfo, string basePath, HttpContext context)
    {
        var baseUrl = GetBaseUrl(context);
        var json = serviceInfo.OpenApiDocument.SerializeAsJson(Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0);
        var mockDocument = new Microsoft.OpenApi.Readers.OpenApiStringReader().Read(json, out _);

        if (serviceInfo.AuthRequirements.Schemes.Any(s => s.Type?.ToLower() == "oauth2"))
        {
            ReplaceOAuthUrls(mockDocument, baseUrl);
        }

        mockDocument.Servers = new List<OpenApiServer>
        {
            new() { Url = $"{baseUrl}{basePath}", Description = "Mock server" },
            new() { Url = basePath, Description = "Mock server (relative)" }
        };

        var authDesc = _authAnalyzer.GetAuthDescription(serviceInfo.AuthRequirements);
        var oauthInfo = serviceInfo.AuthRequirements.Schemes.Any(s => s.Type?.ToLower() == "oauth2")
            ? $"\n\n**OAuth2 Mock Server:**\n- Authorization URL: {baseUrl}/oauth/authorize\n- Token URL: {baseUrl}/oauth/token\n- Client ID: public-client"
            : "";

        mockDocument.Info.Description = $"{mockDocument.Info.Description}\n\n**Mock Server**\n{authDesc}{oauthInfo}";

        return mockDocument;
    }

    private void ReplaceOAuthUrls(OpenApiDocument document, string baseUrl)
    {
        if (document.Components?.SecuritySchemes == null) return;

        foreach (var scheme in document.Components.SecuritySchemes.Values)
        {
            if (scheme.Type == SecuritySchemeType.OAuth2 && scheme.Flows != null)
            {
                if (scheme.Flows.Implicit != null)
                {
                    scheme.Flows.Implicit.AuthorizationUrl = new Uri($"{baseUrl}/oauth/authorize");
                }
                if (scheme.Flows.AuthorizationCode != null)
                {
                    scheme.Flows.AuthorizationCode.AuthorizationUrl = new Uri($"{baseUrl}/oauth/authorize");
                    scheme.Flows.AuthorizationCode.TokenUrl = new Uri($"{baseUrl}/oauth/token");
                }
                if (scheme.Flows.Password != null)
                {
                    scheme.Flows.Password.TokenUrl = new Uri($"{baseUrl}/oauth/token");
                }
                if (scheme.Flows.ClientCredentials != null)
                {
                    scheme.Flows.ClientCredentials.TokenUrl = new Uri($"{baseUrl}/oauth/token");
                }
            }
        }
    }
    private string GenerateSwaggerUiHtml(ServiceInfo serviceInfo, HttpContext context)
    {
        var baseUrl = GetBaseUrl(context);
        var hasOAuth = serviceInfo.AuthRequirements.Schemes.Any(s => s.Type?.ToLower() == "oauth2");
        var authDescription = _authAnalyzer.GetAuthDescription(serviceInfo.AuthRequirements);

        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <title>Swagger UI - {serviceInfo.Name}</title>
    <link rel='stylesheet' href='{baseUrl}/swagger/swagger-ui.css'>
    <style>
        body {{ margin: 0; padding: 20px; }}
        #swagger-ui {{ max-width: 1460px; margin: 0 auto; }}
        .topbar {{ display: none; }}
        .mock-badge {{
            background-color: #27ae60;
            color: white;
            padding: 3px 8px;
            border-radius: 4px;
            margin-left: 10px;
            font-size: 0.6em;
            vertical-align: middle;
        }}
        .info-box {{
            background-color: #f8f9fa;
            border: 1px solid #dee2e6;
            border-radius: 4px;
            padding: 15px;
            margin-bottom: 20px;
        }}
        .auth-info {{
            background-color: {(serviceInfo.AuthRequirements.RequiresAuth ? "#e3f2fd" : "#e8f5e9")};
            border-left: 4px solid {(serviceInfo.AuthRequirements.RequiresAuth ? "#2196f3" : "#4caf50")};
            padding: 10px 15px;
            margin: 10px 0;
            border-radius: 4px;
            font-family: monospace;
            white-space: pre-wrap;
        }}
        .oauth-info {{
            background-color: #fff3e0;
            border-left: 4px solid #ff9800;
            padding: 10px 15px;
            margin: 10px 0;
            border-radius: 4px;
        }}
        .clear-btn {{
            background-color: #ff9800;
            color: white;
            border: none;
            padding: 8px 16px;
            border-radius: 4px;
            cursor: pointer;
            font-size: 14px;
            margin-top: 10px;
        }}
        .clear-btn:hover {{
            background-color: #f57c00;
        }}
        .notification {{
            position: fixed;
            bottom: 20px;
            right: 20px;
            padding: 12px 20px;
            border-radius: 8px;
            z-index: 10000;
            font-family: monospace;
            animation: slideIn 0.3s ease;
            box-shadow: 0 2px 10px rgba(0,0,0,0.2);
        }}
        .notification.success {{ background-color: #4caf50; color: white; }}
        .notification.error {{ background-color: #f44336; color: white; }}
        .notification.info {{ background-color: #2196f3; color: white; }}
        @keyframes slideIn {{
            from {{ transform: translateX(100%); opacity: 0; }}
            to {{ transform: translateX(0); opacity: 1; }}
        }}
    </style>
</head>
<body>
    <div class='info-box'>
        <strong>🚀 Mock server: {serviceInfo.Name}</strong><br>
        <span>Base URL: {baseUrl}{serviceInfo.Name}</span>
    </div>
    
    <div class='auth-info'>
        <strong>{(serviceInfo.AuthRequirements.RequiresAuth ? "🔐" : "🔓")} Authorization info</strong><br>
        {authDescription.Replace("\n", "<br>")}
    </div>
    
    {(hasOAuth ? $@"
    <div class='oauth-info'>
        <strong>🔑 Mock OAuth2/OpenID Connect server</strong><br>
        <span>Base URL: {baseUrl}/oauth</span><br>
        <strong>Test clients:</strong><br>
        • <strong>public-client</strong> (public, no secret) - for Swagger UI<br>
        • <strong>test-client</strong> / test-secret (confidential)<br>
        <br>
        <strong>Usage in Swagger UI:</strong><br>
        1. Click <strong>Authorize</strong> button<br>
        2. Select <strong>OAuth2</strong><br>
        3. Enter <strong>Client ID: public-client</strong><br>
        4. Leave Client Secret empty<br>
        5. Select scopes and click <strong>Authorize</strong><br>
        <br>
        <button class='clear-btn' onclick='clearAuthorization()'>🔓 Clear Authorization</button>
    </div>
    " : "")}
    
    <div id='swagger-ui'></div>
    
    <script src='{baseUrl}/swagger/swagger-ui-bundle.js'></script>
    <script src='{baseUrl}/swagger/swagger-ui-standalone-preset.js'></script>
    <script>
        // Функция для показа уведомлений
        function showNotification(message, type) {{
            var notification = document.createElement('div');
            notification.className = 'notification ' + type;
            notification.textContent = message;
            document.body.appendChild(notification);
            setTimeout(function() {{ notification.remove(); }}, 3000);
        }}
        
        // Функция очистки авторизации
        function clearAuthorization() {{
            // Очищаем localStorage
            localStorage.removeItem('swagger:oauth:token');
            localStorage.removeItem('swagger:oauth:token_type');
            localStorage.removeItem('swagger:oauth:code_verifier');
            localStorage.removeItem('swagger:oauth:code_challenge');
            localStorage.removeItem('swagger:oauth:state');
            
            // Очищаем все ключи, связанные со swagger
            Object.keys(localStorage).forEach(function(key) {{
                if (key.startsWith('swagger:oauth:') || key.includes('authorization')) {{
                    localStorage.removeItem(key);
                }}
            }});
            
            console.log('Authorization cleared from localStorage');
            showNotification('Authorization cleared! Page will reload in 2 seconds.', 'info');
            
            // Перезагружаем страницу
            setTimeout(function() {{
                window.location.reload();
            }}, 2000);
        }}
        
        // Слушаем сообщения от OAuth2 сервера
        window.addEventListener('message', function(event) {{
            if (event.data && event.data.type === 'oauth2' && event.data.params) {{
                var params = event.data.params;
                if (params.access_token) {{
                    localStorage.setItem('swagger:oauth:token', params.access_token);
                    localStorage.setItem('swagger:oauth:token_type', params.token_type || 'Bearer');
                    var authButton = document.querySelector('.auth-wrapper .btn.authorize');
                    if (authButton) authButton.classList.add('locked');
                    showNotification('Authorization successful! Token received.', 'success');
                }}
            }}
        }});
        
        window.onload = function() {{
            console.log('Loading Swagger UI with OpenAPI URL:', '{baseUrl}/{serviceInfo.Name}/openapi');
            console.log('Current token:', localStorage.getItem('swagger:oauth:token'));
            
            const ui = SwaggerUIBundle({{
                url: '{baseUrl}/{serviceInfo.Name}/openapi',
                dom_id: '#swagger-ui',
                deepLinking: true,
                presets: [
                    SwaggerUIBundle.presets.apis,
                    SwaggerUIStandalonePreset
                ],
                plugins: [
                    SwaggerUIBundle.plugins.DownloadUrl
                ],
                layout: 'BaseLayout',
                docExpansion: 'list',
                defaultModelsExpandDepth: -1,
                tryItOutEnabled: true,
                
                servers: [
                    {{
                        url: '{baseUrl}/{serviceInfo.Name}',
                        description: 'Mock server (full URL)'
                    }},
                    {{
                        url: '/{serviceInfo.Name}',
                        description: 'Mock server (relative path)'
                    }}
                ],
                
                {(hasOAuth ? $@"
                oauth2RedirectUrl: '{baseUrl}/swagger/oauth2-redirect.html',
                oauth: {{
                    clientId: 'public-client',
                    appName: 'OpenAPI Mock Server',
                    scopeSeparator: ' ',
                    scopes: ['openid', 'profile', 'email', 'read', 'write'],
                    usePkceWithAuthorizationCodeGrant: false,
                    useBasicAuthenticationWithAccessCodeGrant: false
                }},
                " : "")}
                
                requestInterceptor: (request) => {{
                    // Проверяем, есть ли токен в localStorage
                    var token = localStorage.getItem('swagger:oauth:token');
                    var tokenType = localStorage.getItem('swagger:oauth:token_type') || 'Bearer';
                    
                    // Добавляем заголовок ТОЛЬКО если токен существует
                    if (token && token !== 'null' && token !== 'undefined') {{
                        request.headers = request.headers || {{}};
                        request.headers['Authorization'] = tokenType + ' ' + token;
                        console.log('Adding Authorization header to request:', request.url);
                    }} else {{
                        console.log('No valid token found, request without Authorization header');
                        // Удаляем заголовок Authorization если он был добавлен ранее
                        if (request.headers && request.headers['Authorization']) {{
                            delete request.headers['Authorization'];
                        }}
                    }}
                    
                    return request;
                }},
                
                responseInterceptor: (response) => {{
                    console.log('Response status:', response.status);
                    return response;
                }}
            }});
            
            setTimeout(() => {{
                const titleEl = document.querySelector('.info .title');
                if (titleEl) {{
                    const badge = document.createElement('small');
                    badge.className = 'mock-badge';
                    badge.textContent = 'MOCK SERVER';
                    titleEl.appendChild(badge);
                }}
            }}, 500);
        }};
    </script>
</body>
</html>";
    }
    #endregion

    private class PathParameter
    {
        public string Name { get; set; } = string.Empty;
        public bool Required { get; set; }
        public string Type { get; set; } = "string";
        public string? Format { get; set; }
    }
}