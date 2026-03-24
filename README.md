# OpenAPI Mock Server

A lightweight tool for creating mock servers based on OpenAPI specifications (supports Swagger 2.0 and OpenAPI 3.0). Automatically creates REST API endpoints, generates realistic test data, provides Swagger UI for each service, and includes a built-in mock OAuth2 server.

## Features

- 🚀 **Automatic mock service creation** from OpenAPI specifications (JSON/YAML, URLs, or local files)
- 📚 **Swagger UI** for each service with endpoint testing capabilities
- 🔗 **Multi-service support** - each service on its own path
- 🎲 **Realistic test data generation** considering types, formats, and enums
- 🔒 **Mock authentication** with Bearer Token, API Key, Basic Auth support
- 🔑 **Built-in mock OAuth2/OpenID Connect server** with support for all major flows
- ✅ **Incoming request validation** against OpenAPI schema (data types, required fields, formats)
- 🔄 **Entity relationship support** (relationships)
- 📦 **No database required** - all data generated on-the-fly
- 🐳 **Easy to run** - just dotnet run
- 🔧 **Swagger 2.0 and OpenAPI 3.0 support**
- 📁 **Local file support** - load OpenAPI specifications from JSON or YAML files

## Quick Start

### Requirements

- .NET 8.0 SDK
- Any IDE (Visual Studio 2022, VS Code, Rider)

### Installation

```bash
# Clone repository
git clone https://github.com/yourusername/OpenApiMockServer.git
cd OpenApiMockServer

# Build project
dotnet build

# Run
cd src\OpenApiMockServer.WebApi
dotnet run
```

Server starts at `http://localhost:5000`

### Configuration

Edit `appsettings.json` to add your OpenAPI specifications:

```json
{
  "Services": {
    "my-api": {
      "openApiUrl": "https://example.com/api/swagger.json",
      "basePath": "/api/v1"
    },
    "local-service": {
      "openApiUrl": "./specs/service.yaml",
      "basePath": "/api/service"
    },
    "another-service": {
      "openApiUrl": "C:\\specs\\api.json",
      "basePath": "/api/another"
    }
  }
}
```

| Field | Description | Example |
|-------|-------------|---------|
| `openApiUrl` | URL or file path to OpenAPI specification | `"https://example.com/swagger.json"` or `"./specs/api.yaml"` |
| `basePath` | Base path for endpoints (optional) | `"/api/v1"` |

### Loading from Local Files

The server supports loading OpenAPI specifications from local files in both JSON and YAML formats:

```json
{
  "Services": {
    "json-service": {
      "openApiUrl": "./specs/petstore.json",
      "basePath": "/api/json"
    },
    "yaml-service": {
      "openApiUrl": "./specs/petstore.yaml",
      "basePath": "/api/yaml"
    },
    "absolute-path-service": {
      "openApiUrl": "C:\\projects\\specs\\api.json",
      "basePath": "/api/absolute"
    }
  }
}
```

**Supported file formats:**
- JSON (`.json`)
- YAML (`.yaml`, `.yml`)

**Path formats:**
- Relative paths (e.g., `./specs/api.json`, `../specs/api.yaml`)
- Absolute paths (e.g., `C:\specs\api.json`, `/home/user/specs/api.yaml`)

## Usage

### Available Endpoints

After startup, open `http://localhost:5000` to see the list of all available services.

For each service, the following endpoints are available:

- **Swagger UI**: `http://localhost:5000/{serviceName}/swagger-ui`
- **OpenAPI JSON**: `http://localhost:5000/{serviceName}/openapi`
- **Service Status**: `http://localhost:5000/{serviceName}/_status`
- **Mock Endpoints**: `http://localhost:5000/{serviceName}/{path}` (according to your specification)

### Authentication

The service automatically analyzes security requirements from your OpenAPI specification and applies the appropriate validation.

#### Bearer Token (JWT)
If Bearer auth is specified, use test tokens:
- `valid-token-123`
- `test-token-456`
- `mock-token-789`

```bash
curl -H "Authorization: Bearer valid-token-123" http://localhost:5000/my-api/api/endpoint
```

#### API Key
If API Key is specified, use test keys:
- `test-api-key`
- `special-key`

```bash
# In header
curl -H "X-API-Key: test-api-key" http://localhost:5000/my-api/api/endpoint

# In query parameter
curl "http://localhost:5000/my-api/api/endpoint?api_key=test-api-key"
```

#### Basic Authentication
Accepts any credentials:

```bash
curl -H "Authorization: Basic dXNlcjpwYXNzd29yZA==" http://localhost:5000/my-api/api/endpoint
```

#### OAuth2
Built-in mock OAuth2 server supports:
- Authorization Code Flow
- Implicit Flow
- Client Credentials Flow
- Password Flow

**Test Clients:**
- `public-client` (public, no secret) - for Swagger UI
- `test-client` / `test-secret` (confidential)
- `swagger-ui` / `swagger-secret` (for Swagger UI)

**OpenID Connect Discovery:** `http://localhost:5000/oauth/.well-known/openid-configuration`

### Example Requests

#### Get All Entities

```bash
curl -H "Authorization: Bearer valid-token-123" http://localhost:5000/my-api/api/entity
```

#### Get Entity by ID

```bash
curl -H "api_key: test-api-key" http://localhost:5000/my-api/api/entity/123
```

#### Create New Entity

```bash
curl -X POST http://localhost:5000/my-api/api/entity \
  -H "Authorization: Bearer valid-token-123" \
  -H "Content-Type: application/json" \
  -d '{"name":"Example","status":"active"}'
```

#### Update Entity

```bash
curl -X PUT http://localhost:5000/my-api/api/entity/123 \
  -H "Authorization: Bearer valid-token-123" \
  -H "Content-Type: application/json" \
  -d '{"name":"Updated","status":"inactive"}'
```

#### Delete Entity

```bash
curl -X DELETE http://localhost:5000/my-api/api/entity/123 \
  -H "Authorization: Bearer valid-token-123"
```

#### Get OAuth2 Token

```bash
# Client Credentials Flow
curl -X POST http://localhost:5000/oauth/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials&client_id=test-client&client_secret=test-secret&scope=read write"

# Password Flow
curl -X POST http://localhost:5000/oauth/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password&client_id=test-client&client_secret=test-secret&username=user&password=pass&scope=openid profile"

# Authorization Code Flow (requires code from authorization)
curl -X POST http://localhost:5000/oauth/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=authorization_code&client_id=test-client&client_secret=test-secret&code=AUTH_CODE&redirect_uri=http://localhost:5000/callback"
```

## Documentation

### OpenAPI Formats

Supported:
- OpenAPI 3.0 (JSON, YAML)
- Swagger 2.0 (JSON, YAML)
- URLs or local files

### Response Structure

#### Success Response (200 OK)

```json
{
  "id": "generated-id",
  "name": "Generated Name",
  "status": "active",
  "createdAt": "2024-01-01T12:00:00Z",
  "nestedObject": {
    "property": "value"
  }
}
```

#### Validation Error (400 Bad Request)

```json
{
  "error": "Validation Failed",
  "statusCode": 400,
  "timestamp": "2024-01-01T12:00:00Z",
  "validationErrors": [
    {
      "field": "name",
      "message": "Required field 'name' is missing",
      "expectedType": "string"
    },
    {
      "field": "email",
      "message": "Invalid email format",
      "expectedType": "email",
      "receivedValue": "invalid"
    }
  ]
}
```

#### Authorization Error (401 Unauthorized)

```json
{
  "error": "Unauthorized",
  "details": "Invalid or expired token",
  "statusCode": 401,
  "timestamp": "2024-01-01T12:00:00Z",
  "requiredAuth": "Required authentication: Bearer"
}
```

#### Access Error (403 Forbidden)

```json
{
  "error": "Forbidden",
  "details": "Insufficient permissions",
  "statusCode": 403,
  "timestamp": "2024-01-01T12:00:00Z"
}
```

#### Not Found Error (404 Not Found)

```json
{
  "error": "Not Found",
  "details": "Entity with id '123' not found",
  "statusCode": 404,
  "timestamp": "2024-01-01T12:00:00Z"
}
```

## Architecture

### Key Components

| Component | Description |
|-----------|-------------|
| `OpenApiParser` | Load and parse OpenAPI files (Swagger 2.0 and OpenAPI 3.0) |
| `MockDataGenerator` | Generate test data based on OpenAPI schema |
| `RequestValidator` | Validate incoming requests against schema |
| `AuthService` | Validate authentication (Bearer, API Key, Basic) |
| `AuthAnalyzer` | Analyze security requirements from OpenAPI |
| `OAuthService` | Mock OAuth2/OpenID Connect server |
| `RelationshipResolver` | Determine relationships between entities |
| `MockEndpointBuilder` | Create endpoints based on specification |

### How It Works

1. **Load Specification** - OpenAPI specifications are loaded from configured sources (URLs or local files)
2. **Analyze Requirements** - Security requirements, path parameters, data schemas are analyzed for each service
3. **Register Endpoints** - Endpoints are created for all operations (GET, POST, PUT, DELETE) from the specification
4. **Generate Data** - Fresh test data is generated on each request according to the schema
5. **Validate** - Incoming requests are validated against the schema (types, required fields, formats)
6. **Authenticate** - Tokens/keys are validated according to security requirements
7. **OAuth2** - Built-in OAuth2 server processes token requests

## Development

### Build and Test

```bash
# Build solution
dotnet build

# Run tests
dotnet test

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Adding a New Service

1. Add configuration to `appsettings.json`:
```json
{
  "Services": {
    "new-service": {
      "openApiUrl": "https://example.com/swagger.json",
      "basePath": "/api/new-service"
    }
  }
}
```
2. Restart the application
3. The service will automatically appear on the home page

### Custom Data Generation

You can extend `MockDataGenerator` for custom data generation:

```csharp
public class CustomMockDataGenerator : MockDataGenerator
{
    protected override JsonNode GeneratePrimitiveValue(OpenApiSchema schema, string entityName)
    {
        if (schema.Format == "custom-format")
        {
            return JsonValue.Create(GenerateCustomValue());
        }
        return base.GeneratePrimitiveValue(schema, entityName);
    }
}
```

## Testing

### Running Tests

```bash
# All tests
dotnet test

# Specific test
dotnet test --filter "FullyQualifiedName=OpenApiMockServer.Tests.IntegrationTests.{method}"

# Tests with detailed output
dotnet test --logger "console;verbosity=detailed"
```

### Writing Tests

The project uses xUnit and Moq. Example test:

```csharp
[Fact]
public async Task GetEndpoint_WithValidToken_ReturnsOk()
{
    // Arrange
    _client.DefaultRequestHeaders.Authorization = 
        new AuthenticationHeaderValue("Bearer", "valid-token-123");

    // Act
    var response = await _client.GetAsync("/my-service/api/entity");

    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    
    var content = await response.Content.ReadAsStringAsync();
    var json = JsonDocument.Parse(content).RootElement;
    Assert.Equal(JsonValueKind.Array, json.ValueKind);
}
```

## Troubleshooting

### Swagger UI Not Loading

**Solution**: Check if the service loaded the OpenAPI specification correctly. Request `http://localhost:5000/{serviceName}/_status` - it should have `hasOpenApi: true`. Also check server logs for parsing errors.

### 401 Unauthorized Error

**Solution**: 
1. Check which security requirements are specified in your OpenAPI specification
2. Add the correct authorization header:
   - Bearer: `Authorization: Bearer valid-token-123`
   - API Key: `X-API-Key: test-api-key` or `api_key=test-api-key`
   - Basic: `Authorization: Basic base64(login:password)`

### 405 Method Not Allowed Error

**Solution**: Ensure the method (GET, POST, PUT, DELETE) is supported in your OpenAPI specification. Check server logs for registered endpoints.

### OAuth2 Authorization Not Working in Swagger UI

**Solution**: 
1. Use Client ID: `public-client`
2. Leave Client Secret empty
3. Click "Authorize" and select required scopes
4. After redirect, click "Authorize" on the mock OAuth2 server authorization page
5. If the window doesn't close automatically, close it manually - the token will still be passed

### Data Not Generating

**Solution**: Check the schema in your OpenAPI specification - properties should be defined for entities. If the schema is empty, data will not be generated.

### OpenAPI Parsing Errors

**Solution**: Warnings about non-standard properties appear in logs. These are non-critical and don't affect functionality. If there are critical errors, check the OpenAPI specification for correctness.

## Roadmap

- [ ] WebSocket mock support
- [ ] Data export/import
- [ ] GraphQL support
- [ ] Web UI for mock server management
- [ ] gRPC support
- [ ] Request history storage
- [ ] Data generation considering entity relationships
- [ ] Custom data generator support
- [ ] Generate file on response
- [ ] bind ref entity
- [ ] docker

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## License

MIT License

## Authors

@rewdan
