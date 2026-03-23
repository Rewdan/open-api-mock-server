using System.Text.Json.Nodes;
namespace OpenApiMockServer.WebApi.Models;

public class MockDataStore
{
    private readonly Dictionary<string, List<JsonObject>> _entities = new();
    private readonly Dictionary<string, Dictionary<string, JsonObject>> _entitiesById = new();
    private readonly Dictionary<string, List<EntityRelationship>> _relationships = new();
    private readonly Dictionary<string, Func<JsonObject, string>> _idExtractors = new();

    public void AddEntity(string entityName, JsonObject entity, string? id = null)
    {
        if (!_entities.ContainsKey(entityName))
        {
            _entities[entityName] = new List<JsonObject>();
            _entitiesById[entityName] = new Dictionary<string, JsonObject>();
        }

        _entities[entityName].Add(entity);

        if (!string.IsNullOrEmpty(id))
        {
            _entitiesById[entityName][id] = entity;
        }
        else
        {
            var extractedId = ExtractIdFromEntity(entity, entityName);
            if (!string.IsNullOrEmpty(extractedId))
            {
                _entitiesById[entityName][extractedId] = entity;
            }
        }
    }

    public List<JsonObject> GetEntities(string entityName)
    {
        return _entities.TryGetValue(entityName, out var entities) ? entities : new List<JsonObject>();
    }

    public JsonObject? GetEntityById(string entityName, string id)
    {
        return _entitiesById.TryGetValue(entityName, out var entities)
            ? entities.GetValueOrDefault(id)
            : null;
    }

    public JsonObject? FindEntity(string entityName, Func<JsonObject, bool> predicate)
    {
        if (_entities.TryGetValue(entityName, out var entities))
        {
            return entities.FirstOrDefault(predicate);
        }
        return null;
    }

    public List<JsonObject> FindEntities(string entityName, Func<JsonObject, bool> predicate)
    {
        return _entities.TryGetValue(entityName, out var entities)
            ? entities.Where(predicate).ToList()
            : new List<JsonObject>();
    }

    public bool DeleteEntity(string entityName, string id)
    {
        if (_entitiesById.TryGetValue(entityName, out var entitiesById) && entitiesById.Remove(id))
        {
            _entities[entityName].RemoveAll(e => ExtractIdFromEntity(e, entityName) == id);
            return true;
        }
        return false;
    }

    public bool DeleteEntity(string entityName, Func<JsonObject, bool> predicate)
    {
        if (_entities.TryGetValue(entityName, out var entities))
        {
            var toRemove = entities.Where(predicate).ToList();
            foreach (var entity in toRemove)
            {
                entities.Remove(entity);
                var id = ExtractIdFromEntity(entity, entityName);
                if (!string.IsNullOrEmpty(id) && _entitiesById.ContainsKey(entityName))
                {
                    _entitiesById[entityName].Remove(id);
                }
            }
            return toRemove.Any();
        }
        return false;
    }

    public void UpdateEntity(string entityName, string id, JsonObject updatedEntity)
    {
        if (_entitiesById.TryGetValue(entityName, out var entitiesById) && entitiesById.ContainsKey(id))
        {
            entitiesById[id] = updatedEntity;

            var index = _entities[entityName].FindIndex(e => ExtractIdFromEntity(e, entityName) == id);
            if (index >= 0)
            {
                _entities[entityName][index] = updatedEntity;
            }
        }
    }

    public void UpdateEntity(string entityName, Func<JsonObject, bool> predicate, JsonObject updatedEntity)
    {
        if (_entities.TryGetValue(entityName, out var entities))
        {
            var index = entities.FindIndex(e => predicate(e));
            if (index >= 0)
            {
                var oldEntity = entities[index];
                entities[index] = updatedEntity;

                var oldId = ExtractIdFromEntity(oldEntity, entityName);
                var newId = ExtractIdFromEntity(updatedEntity, entityName);

                if (!string.IsNullOrEmpty(oldId) && _entitiesById.ContainsKey(entityName))
                {
                    _entitiesById[entityName].Remove(oldId);
                    if (!string.IsNullOrEmpty(newId))
                    {
                        _entitiesById[entityName][newId] = updatedEntity;
                    }
                }
            }
        }
    }

    public void AddRelationship(EntityRelationship relationship)
    {
        if (!_relationships.ContainsKey(relationship.SourceEntity))
        {
            _relationships[relationship.SourceEntity] = new List<EntityRelationship>();
        }
        _relationships[relationship.SourceEntity].Add(relationship);
    }

    public List<EntityRelationship> GetRelationships(string entityName)
    {
        return _relationships.TryGetValue(entityName, out var relationships)
            ? relationships
            : new List<EntityRelationship>();
    }

    public void RegisterIdExtractor(string entityName, Func<JsonObject, string> extractor)
    {
        _idExtractors[entityName] = extractor;
    }

    private string ExtractIdFromEntity(JsonObject entity, string entityName)
    {
        if (_idExtractors.TryGetValue(entityName, out var extractor))
        {
            try
            {
                return extractor(entity);
            }
            catch
            {
                // Игнорируем ошибки экстрактора
            }
        }

        var possibleIdFields = new[] { "id", "ID", "Id", "identifier", "Identifier", "uuid", "Uuid", "UUID", "key", "Key" };

        foreach (var field in possibleIdFields)
        {
            if (entity.TryGetPropertyValue(field, out var idValue) && idValue != null)
            {
                return idValue.ToString();
            }
        }

        return string.Empty;
    }

    public void ClearEntity(string entityName)
    {
        if (_entities.ContainsKey(entityName))
        {
            _entities[entityName].Clear();
        }
        if (_entitiesById.ContainsKey(entityName))
        {
            _entitiesById[entityName].Clear();
        }
    }

    public Dictionary<string, int> GetEntityCounts()
    {
        return _entities.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count);
    }
}
