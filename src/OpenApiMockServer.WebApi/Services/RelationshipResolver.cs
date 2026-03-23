using Microsoft.OpenApi.Models;
using OpenApiMockServer.WebApi.Models;

namespace OpenApiMockServer.WebApi.Services;

public class RelationshipResolver
{
    private readonly ILogger<RelationshipResolver> _logger;

    public RelationshipResolver(ILogger<RelationshipResolver> logger)
    {
        _logger = logger;
    }

    public List<EntityRelationship> FindRelationships(OpenApiDocument document, string targetEntity)
    {
        var relationships = new List<EntityRelationship>();

        if (document.Components?.Schemas == null)
            return relationships;

        foreach (var schema in document.Components.Schemas)
        {
            FindRelationshipsInSchema(schema.Value, schema.Key, targetEntity, relationships, document);
        }

        return relationships;
    }

    private void FindRelationshipsInSchema(OpenApiSchema schema, string schemaName, string targetEntity,
        List<EntityRelationship> relationships, OpenApiDocument document, string? parentProperty = null)
    {
        // Проверяем AllOf
        if (schema.AllOf != null)
        {
            foreach (var subSchema in schema.AllOf)
            {
                CheckForReference(subSchema, schemaName, targetEntity, relationships, document, parentProperty, false);

                if (subSchema.AllOf != null)
                {
                    FindRelationshipsInSchema(subSchema, schemaName, targetEntity, relationships, document, parentProperty);
                }
            }
        }

        // Проверяем OneOf
        if (schema.OneOf != null)
        {
            foreach (var subSchema in schema.OneOf)
            {
                CheckForReference(subSchema, schemaName, targetEntity, relationships, document, parentProperty, false);
            }
        }

        // Проверяем AnyOf
        if (schema.AnyOf != null)
        {
            foreach (var subSchema in schema.AnyOf)
            {
                CheckForReference(subSchema, schemaName, targetEntity, relationships, document, parentProperty, false);
            }
        }

        // Проверяем свойства
        if (schema.Properties != null)
        {
            foreach (var prop in schema.Properties)
            {
                var propName = prop.Key;
                var propSchema = prop.Value;

                // Проверяем прямые ссылки
                CheckForReference(propSchema, schemaName, targetEntity, relationships, document, propName, false);

                // Проверяем ссылки в массивах - ЭТО ВАЖНО!
                if (propSchema.Type == "array" && propSchema.Items != null)
                {
                    _logger.LogDebug($"Found array property {schemaName}.{propName} with items");

                    // Проверяем, есть ли ссылка в элементах массива
                    if (propSchema.Items.Reference != null)
                    {
                        var refEntity = propSchema.Items.Reference.Id;
                        if (refEntity == targetEntity)
                        {
                            _logger.LogInformation($"Found array reference relationship: {schemaName}.{propName} -> {targetEntity} (collection)");

                            relationships.Add(new EntityRelationship
                            {
                                SourceEntity = schemaName,
                                SourceProperty = propName,
                                TargetEntity = targetEntity,
                                TargetProperty = "id",
                                IsCollection = true // ВАЖНО: устанавливаем true для массива
                            });
                        }
                    }

                    // Также проверяем через CheckForReference
                    CheckForReference(propSchema.Items, schemaName, targetEntity, relationships, document, propName, true);
                }

                // Проверяем вложенные объекты
                if (propSchema.Properties != null && propSchema.Properties.Any())
                {
                    FindRelationshipsInSchema(propSchema, $"{schemaName}.{propName}", targetEntity, relationships, document);
                }

                // Проверяем AllOf в свойствах
                if (propSchema.AllOf != null)
                {
                    foreach (var subSchema in propSchema.AllOf)
                    {
                        CheckForReference(subSchema, schemaName, targetEntity, relationships, document, propName, false);
                    }
                }
            }
        }

        // Проверяем AdditionalProperties
        if (schema.AdditionalProperties?.Reference != null)
        {
            CheckForReference(schema.AdditionalProperties, schemaName, targetEntity, relationships, document, parentProperty, true);
        }
    }

    private void CheckForReference(OpenApiSchema schema, string schemaName, string targetEntity,
        List<EntityRelationship> relationships, OpenApiDocument document, string? propertyName = null, bool isCollection = false)
    {
        // Прямая ссылка
        if (schema.Reference != null)
        {
            var refEntity = schema.Reference.Id;
            if (refEntity == targetEntity)
            {
                _logger.LogDebug($"Found reference relationship: {schemaName}.{propertyName} -> {targetEntity} (collection={isCollection})");

                relationships.Add(new EntityRelationship
                {
                    SourceEntity = schemaName,
                    SourceProperty = propertyName ?? "_reference",
                    TargetEntity = targetEntity,
                    TargetProperty = "id",
                    IsCollection = isCollection
                });
            }
        }

        // Разрешаем ссылку и проверяем внутри
        if (schema.Reference != null && document.Components?.Schemas != null)
        {
            var refId = schema.Reference.Id;
            if (document.Components.Schemas.TryGetValue(refId, out var resolvedSchema))
            {
                FindRelationshipsInSchema(resolvedSchema, schemaName, targetEntity, relationships, document, propertyName);
            }
        }
    }

    public Dictionary<string, List<EntityRelationship>> FindAllRelationships(OpenApiDocument document)
    {
        var allRelationships = new Dictionary<string, List<EntityRelationship>>();

        if (document.Components?.Schemas == null)
            return allRelationships;

        foreach (var schema in document.Components.Schemas)
        {
            var entityName = schema.Key;
            var relationships = FindRelationships(document, entityName);

            if (relationships.Any())
            {
                allRelationships[entityName] = relationships;
            }
        }

        return allRelationships;
    }
}