using System;
using System.Text.Json;

namespace CrudQL.Service.Validation;

public sealed class CrudFilterContext
{
    public CrudFilterContext(string entityName, Type entityType, JsonElement? filter)
    {
        EntityName = entityName;
        EntityType = entityType;
        Filter = filter;
    }

    public string EntityName { get; }

    public Type EntityType { get; }

    public JsonElement? Filter { get; }
}
