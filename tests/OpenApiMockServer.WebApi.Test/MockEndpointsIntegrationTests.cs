using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace OpenApiMockServer.WebApi.Test;

public class MockEndpointsIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _fixturesPath;

    public MockEndpointsIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _fixturesPath = Path.Combine(AppContext.BaseDirectory, "Fixtures");

        // Создаем папку Fixtures если её нет
        if (!Directory.Exists(_fixturesPath))
        {
            Directory.CreateDirectory(_fixturesPath);
        }

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration(conf =>
            {
                conf.Sources.Clear();
                conf.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.test.json"));
                // Добавляем конфигурацию из переменных окружения
                conf.AddEnvironmentVariables();

                // Добавляем конфигурацию из InMemory коллекции для специфичных тестовых значений
                conf.AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["TestMode"] = "true",
                    ["FixturesPath"] = _fixturesPath
                });
            });
        });

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GetRoot_ReturnsServiceList()
    {
        // Act
        var response = await _client.GetAsync("/");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content).RootElement;

        Assert.True(json.TryGetProperty("services", out var services));
        Assert.True(json.TryGetProperty("oauth", out _));
    }

    [Fact]
    public async Task GetServiceRoot_ReturnsServiceInfo()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token-123");

        // Act
        var response = await _client.GetAsync("/petstore");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content).RootElement;

        Assert.Equal("petstore", json.GetProperty("service").GetString());
        Assert.Equal("active", json.GetProperty("status").GetString());
        Assert.Equal("mock", json.GetProperty("type").GetString());
    }

    [Fact]
    public async Task GetSwaggerUi_ReturnsHtmlPage()
    {
        // Act
        var response = await _client.GetAsync("/petstore/swagger-ui");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Swagger UI", content);
        Assert.Contains("petstore", content);
    }

    [Fact]
    public async Task GetOpenApiJson_ReturnsOpenApiDocument()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token-123");

        // Act
        var response = await _client.GetAsync("/petstore/openapi");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content).RootElement;

        Assert.True(json.TryGetProperty("openapi", out _));
        Assert.Equal("Swagger Petstore", json.GetProperty("info").GetProperty("title").GetString());
    }

    [Fact]
    public async Task GetStatus_ReturnsHealthStatus()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token-123");

        // Act
        var response = await _client.GetAsync("/petstore/_status");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content).RootElement;

        Assert.Equal("healthy", json.GetProperty("status").GetString());
        Assert.Equal("petstore", json.GetProperty("service").GetString());
    }


    #region Authentication Tests

    [Fact]
    public async Task GetPets_WithoutAuth_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/petstore/pet/1");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content).RootElement;

        Assert.Equal("Unauthorized", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetPets_WithValidToken_ReturnsOk()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token-123");
        var response2 = await _client.GetAsync("");

        // Act
        var response = await _client.GetAsync("/petstore/pet/1");
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content).RootElement;

        Assert.Equal(JsonValueKind.Object, json.ValueKind);
    }

    [Fact]
    public async Task GetPets_WithInvalidToken_ReturnsUnauthorized()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");

        // Act
        var response = await _client.GetAsync("/petstore/pet/1");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPets_WithWrongAuthFormat_ReturnsUnauthorized()
    {
        // Arrange
       // _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("", "");

        // Act
        var response = await _client.GetAsync("/petstore/pet/1");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region CRUD Tests

    [Fact]
    public async Task GetPetById_WithValidId_ReturnsPet()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token-123");

        // Act
        var response = await _client.GetAsync("/petstore/pet/123");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content).RootElement;

        Assert.Equal(JsonValueKind.Object, json.ValueKind);
        Assert.True(json.TryGetProperty("name", out _));
        Assert.True(json.TryGetProperty("status", out _));
    }

    [Fact]
    public async Task PostPet_WithValidData_ReturnsCreated()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token-123");

        var content = new StringContent("{\r\n  \"id\": 0,\r\n  \"category\": {\r\n    \"id\": 0,\r\n    \"name\": \"string\"\r\n  },\r\n  \"name\": \"doggie\",\r\n  \"photoUrls\": [\r\n    \"string\"\r\n  ],\r\n  \"tags\": [\r\n    {\r\n      \"id\": 0,\r\n      \"name\": \"string\"\r\n    }\r\n  ],\r\n  \"status\": \"available\"\r\n}", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/petstore/pet", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseContent).RootElement;

        Assert.Equal(JsonValueKind.Object, json.ValueKind);
        Assert.True(json.TryGetProperty("name", out var name));
        Assert.NotEmpty(name.GetString());
    }

    [Fact]
    public async Task PostPet_WithMissingRequiredField_ReturnsBadRequest()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token-123");

        var invalidPet = new { status = "available" }; // name is required
        var content = new StringContent(JsonSerializer.Serialize(invalidPet), Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/petstore/pet", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseContent).RootElement;

        Assert.Equal("Validation Failed", json.GetProperty("error").GetString());
        Assert.True(json.TryGetProperty("validationErrors", out var errors));
        Assert.True(errors.GetArrayLength() > 0);
    }

    [Fact]
    public async Task PostPet_WithInvalidEnum_ReturnsBadRequest()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token-123");

        var invalidPet = new { name = "Test", status = "invalid_status" };
        var content = new StringContent(JsonSerializer.Serialize(invalidPet), Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/petstore/pet", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutPet_WithValidData_ReturnsOk()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token-123");

        var content = new StringContent("{\r\n  \"id\": 0,\r\n  \"category\": {\r\n    \"id\": 0,\r\n    \"name\": \"string\"\r\n  },\r\n  \"name\": \"doggie\",\r\n  \"photoUrls\": [\r\n    \"string\"\r\n  ],\r\n  \"tags\": [\r\n    {\r\n      \"id\": 0,\r\n      \"name\": \"string\"\r\n    }\r\n  ],\r\n  \"status\": \"available\"\r\n}", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PutAsync("/petstore/pet", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseContent).RootElement;

        Assert.True(json.TryGetProperty("name", out var name));
        Assert.NotEmpty(name.GetString());
    }

    [Fact]
    public async Task DeletePet_ReturnsNoContent()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token-123");

        // Act
        var response = await _client.DeleteAsync("/petstore/pet/123");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task PostPet_WithInvalidJson_ReturnsBadRequest()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token-123");

        var content = new StringContent("{ invalid json }", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/petstore/pet", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetNonExistentEndpoint_ReturnsNotFound()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token-123");

        // Act
        var response = await _client.GetAsync("/petstore/api/petstore/nonexistent");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region OAuth2 Tests

    [Fact]
    public async Task OAuthWellKnownConfiguration_ReturnsConfig()
    {
        // Act
        var response = await _client.GetAsync("/oauth/.well-known/openid-configuration");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content).RootElement;

        Assert.True(json.TryGetProperty("issuer", out _));
        Assert.True(json.TryGetProperty("authorization_endpoint", out _));
        Assert.True(json.TryGetProperty("token_endpoint", out _));
    }

    [Fact]
    public async Task OAuthJwks_ReturnsKeys()
    {
        // Act
        var response = await _client.GetAsync("/oauth/jwks");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content).RootElement;

        Assert.True(json.TryGetProperty("keys", out var keys));
        Assert.True(keys.GetArrayLength() > 0);
    }

    [Fact]
    public async Task OAuthToken_WithClientCredentials_ReturnsToken()
    {
        // Arrange
        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "test-client",
            ["client_secret"] = "test-secret",
            ["scope"] = "read write"
        };

        var content = new FormUrlEncodedContent(formData);

        // Act
        var response = await _client.PostAsync("/oauth/token", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var responseContent = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseContent).RootElement;

        Assert.True(json.TryGetProperty("access_token", out var token));
        Assert.False(string.IsNullOrEmpty(token.GetString()));
        Assert.Equal("Bearer", json.GetProperty("token_type").GetString());
        Assert.True(json.GetProperty("expires_in").GetInt32() > 0);
    }

    [Fact]
    public async Task OAuthUserInfo_WithValidToken_ReturnsUserInfo()
    {
        // Arrange
        // Сначала получаем токен
        var tokenResponse = await GetTokenAsync();
        var accessToken = tokenResponse.GetProperty("access_token").GetString();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // Act
        var response = await _client.PostAsync("/oauth/userinfo", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content).RootElement;

        Assert.True(json.TryGetProperty("sub", out _));
        Assert.True(json.TryGetProperty("name", out _));
        Assert.True(json.TryGetProperty("email", out _));
    }

    [Fact]
    public async Task OAuthUserInfo_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.PostAsync("/oauth/userinfo", null);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content).RootElement;

        Assert.Equal("invalid_token", json.GetProperty("error").GetString());
    }

    #endregion

    #region Helper Methods

    private async Task<JsonElement> GetTokenAsync()
    {
        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "test-client",
            ["client_secret"] = "test-secret",
            ["scope"] = "read write"
        };

        var content = new FormUrlEncodedContent(formData);
        var response = await _client.PostAsync("/oauth/token", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        return JsonDocument.Parse(responseContent).RootElement;
    }

    #endregion
}