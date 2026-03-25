using Bogus;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Any;
using System.Text.Json.Nodes;

namespace OpenApiMockServer.WebApi.Services;

public class MockDataGenerator
{
    private readonly ILogger<MockDataGenerator> _logger;
    private readonly Dictionary<string, Faker> _fakers = new();
    private const int MAX_DEPTH = 3; // Максимальная глубина рекурсии
    private const int MAX_ARRAY_SIZE = 5; // Максимальный размер массива

    public MockDataGenerator(ILogger<MockDataGenerator> logger)
    {
        _logger = logger;
    }

    public JsonObject GenerateObject(OpenApiSchema schema, string entityName, OpenApiDocument? document = null, int depth = 0)
    {
        try
        {
            // Проверка глубины рекурсии
            if (depth >= MAX_DEPTH)
            {
                _logger.LogDebug($"Reached max depth ({MAX_DEPTH}) for {entityName}, returning empty object");
                return new JsonObject();
            }

            var resolvedSchema = ResolveSchema(schema, document);
            if (resolvedSchema?.Properties == null || !resolvedSchema.Properties.Any())
            {
                _logger.LogDebug($"Schema for {entityName} has no properties");
                return new JsonObject();
            }

            var result = new JsonObject();
            var faker = GetOrCreateFaker(entityName);

            foreach (var prop in resolvedSchema.Properties)
            {
                var propName = prop.Key;
                var propSchema = prop.Value;

                // Пропускаем readOnly поля
                if (propSchema.ReadOnly)
                {
                    continue;
                }

                try
                {
                    var value = GenerateValue(propSchema, $"{entityName}.{propName}", document, depth + 1);
                    if (value != null)
                    {
                        result[propName] = value;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error generating field {propName} for {entityName}");
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error generating object for {entityName}");
            return new JsonObject();
        }
    }

    public JsonArray GenerateArray(OpenApiSchema schema, string entityName, OpenApiDocument? document = null, int depth = 0)
    {
        try
        {
            // Проверка глубины рекурсии
            if (depth >= MAX_DEPTH)
            {
                _logger.LogDebug($"Reached max depth ({MAX_DEPTH}) for array {entityName}, returning empty array");
                return new JsonArray();
            }

            var resolvedSchema = ResolveSchema(schema, document);
            var itemsSchema = resolvedSchema?.Items;
            if (itemsSchema == null)
            {
                return new JsonArray();
            }

            var array = new JsonArray();

            // Генерируем от 2 до MAX_ARRAY_SIZE элементов
            var count = new Random().Next(2, MAX_ARRAY_SIZE + 1);

            for (int i = 0; i < count; i++)
            {
                try
                {
                    var item = GenerateValue(itemsSchema, $"{entityName}[{i}]", document, depth + 1);
                    if (item != null)
                    {
                        array.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error generating array element {entityName}[{i}]");
                }
            }

            return array;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error generating array for {entityName}");
            return new JsonArray();
        }
    }

    public JsonNode? GenerateValue(OpenApiSchema schema, string entityName, OpenApiDocument? document = null, int depth = 0)
    {
        try
        {
            // Проверка глубины рекурсии
            if (depth >= MAX_DEPTH)
            {
                _logger.LogDebug($"Reached max depth ({MAX_DEPTH}) for {entityName}, returning null");
                return null;
            }

            var resolvedSchema = ResolveSchema(schema, document);
            if (resolvedSchema == null)
            {
                return null;
            }

            // Обработка enum
            if (resolvedSchema.Enum != null && resolvedSchema.Enum.Any())
            {
                return GenerateEnumValue(resolvedSchema.Enum, entityName);
            }

            // Обработка объекта
            if (resolvedSchema.Properties != null && resolvedSchema.Properties.Any())
            {
                return GenerateObject(resolvedSchema, entityName, document, depth);
            }

            // Обработка массива
            if (resolvedSchema.Type == "array")
            {
                return GenerateArray(resolvedSchema, entityName, document, depth);
            }

            // Обработка примитивных типов
            return GeneratePrimitiveValue(resolvedSchema, entityName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error generating value for {entityName}");
            return null;
        }
    }

    private JsonNode? GenerateEnumValue(IList<IOpenApiAny> enumValues, string entityName)
    {
        try
        {
            var random = new Random();
            var index = random.Next(0, enumValues.Count);
            var enumValue = enumValues[index];

            return ConvertOpenApiAnyToJsonNode(enumValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error generating enum value for {entityName}");
            return JsonValue.Create(string.Empty);
        }
    }

    private JsonNode? GeneratePrimitiveValue(OpenApiSchema schema, string entityName)
    {
        try
        {
            var faker = GetOrCreateFaker(entityName);

            object value = schema.Type switch
            {
                "string" when schema.Format == "uuid" || schema.Format == "guid" =>
                    Guid.NewGuid().ToString("D"),

                "string" when schema.Format == "email" =>
                    faker.Internet.Email(),

                "string" when schema.Format == "uri" =>
                    faker.Internet.Url(),

                "string" when schema.Format == "date" =>
                    faker.Date.Past().ToString("yyyy-MM-dd"),

                "string" when schema.Format == "date-time" =>
                    faker.Date.Past().ToString("o"),

                "string" when schema.Format == "phone" =>
                    faker.Phone.PhoneNumber(),

                "string" when schema.Pattern != null =>
                    GeneratePatternValue(schema.Pattern, faker),

                "string" =>
                    faker.Lorem.Word(),

                "integer" when schema.Format == "int64" =>
                    GenerateIntegerWithRange(schema, faker, true),

                "integer" =>
                    GenerateIntegerWithRange(schema, faker, false),

                "number" when schema.Format == "double" || schema.Format == "float" =>
                    GenerateNumberWithRange(schema, faker),

                "number" =>
                    GenerateNumberWithRange(schema, faker),

                "boolean" =>
                    faker.Random.Bool(),

                _ => null
            };

            if (value == null)
            {
                return null;
            }

            return JsonValue.Create(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error generating primitive value for {entityName}");
            return JsonValue.Create(string.Empty);
        }
    }

    private long GenerateIntegerWithRange(OpenApiSchema schema, Faker faker, bool isLong)
    {
        int min = schema.Minimum.HasValue ? (int)schema.Minimum.Value : 1;
        int max = schema.Maximum.HasValue ? (int)schema.Maximum.Value : (isLong ? 1000000 : 1000);

        // Убеждаемся, что min < max
        if (min >= max)
        {
            return min;
        }

        return faker.Random.Int(min, max);
    }

    private double GenerateNumberWithRange(OpenApiSchema schema, Faker faker)
    {
        double min = schema.Minimum.HasValue ? (double)schema.Minimum.Value : 1.0;
        double max = schema.Maximum.HasValue ? (double)schema.Maximum.Value : 1000.0;

        // Убеждаемся, что min < max
        if (min >= max)
        {
            return min;
        }

        return Math.Round(faker.Random.Double(min, max), 2);
    }

    private JsonNode? ConvertOpenApiAnyToJsonNode(IOpenApiAny openApiAny)
    {
        return openApiAny switch
        {
            OpenApiString openApiString => JsonValue.Create(openApiString.Value),
            OpenApiInteger openApiInteger => JsonValue.Create(openApiInteger.Value),
            OpenApiLong openApiLong => JsonValue.Create(openApiLong.Value),
            OpenApiFloat openApiFloat => JsonValue.Create(openApiFloat.Value),
            OpenApiDouble openApiDouble => JsonValue.Create(openApiDouble.Value),
           // OpenApiDecimal openApiDecimal => JsonValue.Create(openApiDecimal.Value),
            OpenApiBoolean openApiBoolean => JsonValue.Create(openApiBoolean.Value),
            OpenApiDateTime openApiDateTime => JsonValue.Create(openApiDateTime.Value.ToString("o")),
            OpenApiDate openApiDate => JsonValue.Create(openApiDate.Value.ToString("yyyy-MM-dd")),
            OpenApiObject openApiObject => ConvertOpenApiObjectToJsonObject(openApiObject),
            OpenApiArray openApiArray => ConvertOpenApiArrayToJsonArray(openApiArray),
            OpenApiNull _ => null,
            _ => JsonValue.Create(openApiAny?.ToString() ?? string.Empty)
        };
    }

    private JsonObject? ConvertOpenApiObjectToJsonObject(OpenApiObject openApiObject)
    {
        var result = new JsonObject();

        foreach (var kvp in openApiObject)
        {
            var convertedValue = ConvertOpenApiAnyToJsonNode(kvp.Value);
            if (convertedValue != null)
            {
                result[kvp.Key] = convertedValue;
            }
        }

        return result;
    }

    private JsonArray? ConvertOpenApiArrayToJsonArray(OpenApiArray openApiArray)
    {
        var result = new JsonArray();

        foreach (var item in openApiArray)
        {
            var convertedItem = ConvertOpenApiAnyToJsonNode(item);
            if (convertedItem != null)
            {
                result.Add(convertedItem);
            }
        }

        return result;
    }

    private OpenApiSchema? ResolveSchema(OpenApiSchema schema, OpenApiDocument? document)
    {
        if (schema.Reference != null && document?.Components?.Schemas != null)
        {
            var refId = schema.Reference.Id;
            if (document.Components.Schemas.TryGetValue(refId, out var resolvedSchema))
            {
                return resolvedSchema;
            }
        }
        return schema;
    }

    private Faker GetOrCreateFaker(string entityName)
    {
        if (!_fakers.ContainsKey(entityName))
        {
            _fakers[entityName] = new Faker();
        }
        return _fakers[entityName];
    }

    private string GeneratePatternValue(string pattern, Faker faker)
    {
        return $"gen_{faker.Random.AlphaNumeric(8)}";
    }
}