using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Any;
using System.Text.Json.Nodes;
using OpenApiMockServer.WebApi.Models;

namespace OpenApiMockServer.WebApi.Services;

public class RequestValidator
{
    private readonly ILogger<RequestValidator> _logger;

    public RequestValidator(ILogger<RequestValidator> logger)
    {
        _logger = logger;
    }

    public List<ValidationError> ValidateRequest(JsonObject requestBody, OpenApiSchema requestSchema, OpenApiDocument? document)
    {
        var errors = new List<ValidationError>();

        if (requestBody == null)
        {
            errors.Add(new ValidationError
            {
                Field = "body",
                Message = "Request body is required",
                ExpectedType = "object"
            });
            return errors;
        }

        var resolvedSchema = ResolveSchema(requestSchema, document);
        if (resolvedSchema?.Properties == null)
        {
            return errors;
        }
        var dict = new Dictionary<string,OpenApiSchema>(resolvedSchema.Properties);
        // Проверка обязательных полей
        if (resolvedSchema.Required != null)
        {
            foreach (var required in resolvedSchema.Required)
            {
                if (!requestBody.ContainsKey(required))
                {
                    errors.Add(new ValidationError
                    {
                        Field = required,
                        Message = $"Required field '{required}' is missing",
                        ExpectedType = GetSchemaType(dict.GetValueOrDefault(required))
                    });
                }
            }
        }

        // Проверка типов полей
        foreach (var prop in resolvedSchema.Properties)
        {
            var propName = prop.Key;
            var propSchema = prop.Value;

            if (requestBody.TryGetPropertyValue(propName, out var value) && value != null)
            {
                ValidateField(propName, value, propSchema, document, errors);
            }
        }

        // Проверка на лишние поля (не указанные в схеме)
        foreach (var prop in requestBody)
        {
            if (!resolvedSchema.Properties.ContainsKey(prop.Key))
            {
                errors.Add(new ValidationError
                {
                    Field = prop.Key,
                    Message = $"Unexpected field '{prop.Key}' - not defined in schema",
                    ReceivedValue = prop.Value?.ToString()
                });
            }
        }

        return errors;
    }

    private void ValidateField(string fieldName, JsonNode value, OpenApiSchema schema, OpenApiDocument? document, List<ValidationError> errors)
    {
        var resolvedSchema = ResolveSchema(schema, document);

        // Проверка enum
        if (resolvedSchema?.Enum != null && resolvedSchema.Enum.Any())
        {
            if (!IsValidEnumValue(value, resolvedSchema.Enum))
            {
                errors.Add(new ValidationError
                {
                    Field = fieldName,
                    Message = $"Value is not in allowed enum values",
                    ExpectedType = "enum",
                    ReceivedValue = value.ToString()
                });
            }
            return;
        }

        switch (resolvedSchema?.Type)
        {
            case "string":
                if (value is not JsonValue jsonStringValue || !jsonStringValue.TryGetValue<string>(out _))
                {
                    errors.Add(new ValidationError
                    {
                        Field = fieldName,
                        Message = $"Expected string, got {GetJsonNodeType(value)}",
                        ExpectedType = "string",
                        ReceivedValue = value.ToString()
                    });
                }
                else
                {
                    // Дополнительная валидация по формату
                    var stringValue = value.ToString();
                    ValidateStringFormat(fieldName, stringValue, resolvedSchema, errors);
                }
                break;

            case "integer":
                if (value is JsonValue jsonIntValue)
                {
                    if (!jsonIntValue.TryGetValue<int>(out _) && !jsonIntValue.TryGetValue<long>(out _))
                    {
                        errors.Add(new ValidationError
                        {
                            Field = fieldName,
                            Message = $"Expected integer, got {GetJsonNodeType(value)}",
                            ExpectedType = "integer",
                            ReceivedValue = value.ToString()
                        });
                    }
                }
                else
                {
                    errors.Add(new ValidationError
                    {
                        Field = fieldName,
                        Message = $"Expected integer, got {GetJsonNodeType(value)}",
                        ExpectedType = "integer",
                        ReceivedValue = value.ToString()
                    });
                }
                break;

            case "number":
                if (value is JsonValue jsonNumberValue)
                {
                    if (!jsonNumberValue.TryGetValue<int>(out _) &&
                        !jsonNumberValue.TryGetValue<long>(out _) &&
                        !jsonNumberValue.TryGetValue<double>(out _) &&
                        !jsonNumberValue.TryGetValue<decimal>(out _))
                    {
                        errors.Add(new ValidationError
                        {
                            Field = fieldName,
                            Message = $"Expected number, got {GetJsonNodeType(value)}",
                            ExpectedType = "number",
                            ReceivedValue = value.ToString()
                        });
                    }
                }
                else
                {
                    errors.Add(new ValidationError
                    {
                        Field = fieldName,
                        Message = $"Expected number, got {GetJsonNodeType(value)}",
                        ExpectedType = "number",
                        ReceivedValue = value.ToString()
                    });
                }
                break;

            case "boolean":
                if (value is JsonValue jsonBoolValue)
                {
                    if (!jsonBoolValue.TryGetValue<bool>(out _))
                    {
                        errors.Add(new ValidationError
                        {
                            Field = fieldName,
                            Message = $"Expected boolean, got {GetJsonNodeType(value)}",
                            ExpectedType = "boolean",
                            ReceivedValue = value.ToString()
                        });
                    }
                }
                else
                {
                    errors.Add(new ValidationError
                    {
                        Field = fieldName,
                        Message = $"Expected boolean, got {GetJsonNodeType(value)}",
                        ExpectedType = "boolean",
                        ReceivedValue = value.ToString()
                    });
                }
                break;

            case "array":
                if (value is not JsonArray)
                {
                    errors.Add(new ValidationError
                    {
                        Field = fieldName,
                        Message = $"Expected array, got {GetJsonNodeType(value)}",
                        ExpectedType = "array",
                        ReceivedValue = value.ToString()
                    });
                }
                else if (resolvedSchema.Items != null)
                {
                    // Валидация элементов массива
                    var array = value as JsonArray;
                    for (int i = 0; i < array?.Count; i++)
                    {
                        ValidateField($"{fieldName}[{i}]", array[i]!, resolvedSchema.Items, document, errors);
                    }
                }
                break;

            case "object":
                if (value is not JsonObject)
                {
                    errors.Add(new ValidationError
                    {
                        Field = fieldName,
                        Message = $"Expected object, got {GetJsonNodeType(value)}",
                        ExpectedType = "object",
                        ReceivedValue = value.ToString()
                    });
                }
                else if (resolvedSchema.Properties != null)
                {
                    // Рекурсивная валидация вложенного объекта
                    var obj = value as JsonObject;
                    var nestedErrors = ValidateRequest(obj!, resolvedSchema, document);
                    errors.AddRange(nestedErrors.Select(e =>
                    {
                        e.Field = $"{fieldName}.{e.Field}";
                        return e;
                    }));
                }
                break;
        }
    }

    private void ValidateStringFormat(string fieldName, string value, OpenApiSchema schema, List<ValidationError> errors)
    {
        switch (schema.Format)
        {
            case "email":
                if (!IsValidEmail(value))
                {
                    errors.Add(new ValidationError
                    {
                        Field = fieldName,
                        Message = $"Invalid email format",
                        ExpectedType = "email",
                        ReceivedValue = value
                    });
                }
                break;

            case "uuid":
            case "guid":
                if (!Guid.TryParse(value, out _))
                {
                    errors.Add(new ValidationError
                    {
                        Field = fieldName,
                        Message = $"Invalid UUID format",
                        ExpectedType = "uuid",
                        ReceivedValue = value
                    });
                }
                break;

            case "uri":
                if (!Uri.TryCreate(value, UriKind.Absolute, out _))
                {
                    errors.Add(new ValidationError
                    {
                        Field = fieldName,
                        Message = $"Invalid URI format",
                        ExpectedType = "uri",
                        ReceivedValue = value
                    });
                }
                break;

            case "date":
                if (!DateOnly.TryParse(value, out _))
                {
                    errors.Add(new ValidationError
                    {
                        Field = fieldName,
                        Message = $"Invalid date format (expected YYYY-MM-DD)",
                        ExpectedType = "date",
                        ReceivedValue = value
                    });
                }
                break;

            case "date-time":
                if (!DateTime.TryParse(value, out _))
                {
                    errors.Add(new ValidationError
                    {
                        Field = fieldName,
                        Message = $"Invalid date-time format",
                        ExpectedType = "date-time",
                        ReceivedValue = value
                    });
                }
                break;
        }

        // Проверка паттерна
        if (!string.IsNullOrEmpty(schema.Pattern))
        {
            var regex = new System.Text.RegularExpressions.Regex(schema.Pattern);
            if (!regex.IsMatch(value))
            {
                errors.Add(new ValidationError
                {
                    Field = fieldName,
                    Message = $"Value does not match pattern: {schema.Pattern}",
                    ExpectedType = "pattern",
                    ReceivedValue = value
                });
            }
        }

        // Проверка minLength/maxLength
        if (schema.MinLength.HasValue && value.Length < schema.MinLength.Value)
        {
            errors.Add(new ValidationError
            {
                Field = fieldName,
                Message = $"String length {value.Length} is less than minimum {schema.MinLength.Value}",
                ExpectedType = $"string(min={schema.MinLength.Value})",
                ReceivedValue = value
            });
        }

        if (schema.MaxLength.HasValue && value.Length > schema.MaxLength.Value)
        {
            errors.Add(new ValidationError
            {
                Field = fieldName,
                Message = $"String length {value.Length} exceeds maximum {schema.MaxLength.Value}",
                ExpectedType = $"string(max={schema.MaxLength.Value})",
                ReceivedValue = value
            });
        }
    }

    private bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    private bool IsValidEnumValue(JsonNode value, IList<IOpenApiAny> enumValues)
    {
        var stringValue = value.ToString();

        foreach (var enumValue in enumValues)
        {
            if (enumValue is OpenApiString openApiString && openApiString.Value == stringValue)
            {
                return true;
            }
            if (enumValue is OpenApiInteger openApiInt && openApiInt.Value.ToString() == stringValue)
            {
                return true;
            }
            if (enumValue is OpenApiLong openApiLong && openApiLong.Value.ToString() == stringValue)
            {
                return true;
            }
            if (enumValue is OpenApiBoolean openApiBool && openApiBool.Value.ToString().ToLower() == stringValue.ToLower())
            {
                return true;
            }
        }

        return false;
    }

    private string GetJsonNodeType(JsonNode node)
    {
        return node switch
        {
            JsonObject => "object",
            JsonArray => "array",
            JsonValue value => GetJsonValueType(value),
            _ => "unknown"
        };
    }

    private string GetJsonValueType(JsonValue value)
    {
        if (value.TryGetValue<string>(out _)) return "string";
        if (value.TryGetValue<int>(out _)) return "integer";
        if (value.TryGetValue<long>(out _)) return "integer";
        if (value.TryGetValue<double>(out _)) return "number";
        if (value.TryGetValue<decimal>(out _)) return "number";
        if (value.TryGetValue<bool>(out _)) return "boolean";
        return "unknown";
    }

    private string GetSchemaType(OpenApiSchema? schema)
    {
        if (schema == null) return "unknown";

        if (schema.Enum?.Any() == true) return "enum";
        if (!string.IsNullOrEmpty(schema.Format)) return $"{schema.Type}({schema.Format})";
        return schema.Type ?? "unknown";
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
}