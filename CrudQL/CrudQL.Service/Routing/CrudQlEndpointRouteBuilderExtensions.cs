using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CrudQL.Service.Routing;

public static class CrudQlEndpointRouteBuilderExtensions
{
    private const string CrudRoute = "/crud";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapCrudQl(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(CrudRoute, HandleCreate);
        endpoints.MapGet(CrudRoute, HandleRead);
        endpoints.MapPut(CrudRoute, HandleUpdate);
        endpoints.MapDelete(CrudRoute, HandleDelete);

        return endpoints;
    }

    private static async Task HandleCreate(HttpContext context, [FromServices] CrudRuntimeStore store)
    {
        var root = await ParseBodyAsync(context.Request);
        if (root == null)
        {
            await Results.BadRequest(new { message = "Payload must be a JSON object" }).ExecuteAsync(context);
            return;
        }

        if (!TryResolveEntity(context, root.Value, out var entityName, out var error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        if (!root.Value.TryGetProperty("input", out var inputElement) || inputElement.ValueKind != JsonValueKind.Object)
        {
            await Results.BadRequest(new { message = "Missing 'input' object for create operation" }).ExecuteAsync(context);
            return;
        }

        var returning = GetStringArray(root.Value, "returning");
        var record = store.GetStore(entityName).Create(inputElement, returning);
        await Results.Json(new { data = record }, SerializerOptions, null, StatusCodes.Status201Created).ExecuteAsync(context);
    }

    private static async Task HandleRead(HttpContext context, [FromServices] CrudRuntimeStore store)
    {
        JsonElement? root = null;
        if (context.Request.ContentLength > 0 || context.Request.Body.CanSeek && context.Request.Body.Length > 0)
        {
            root = await ParseBodyAsync(context.Request);
            if (root == null)
            {
                await Results.BadRequest(new { message = "Payload must be a JSON object" }).ExecuteAsync(context);
                return;
            }
        }

        if (!TryResolveEntity(context, root, out var entityName, out var error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        var select = ResolveSelectFields(root, context.Request.Query);
        var records = store.Read(entityName, select);
        await Results.Json(new { data = records }, SerializerOptions, null, StatusCodes.Status200OK).ExecuteAsync(context);
    }

    private static async Task HandleUpdate(HttpContext context, [FromServices] CrudRuntimeStore store)
    {
        var root = await ParseBodyAsync(context.Request);
        if (root == null)
        {
            await Results.BadRequest(new { message = "Payload must be a JSON object" }).ExecuteAsync(context);
            return;
        }

        if (!TryResolveEntity(context, root.Value, out var entityName, out var error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        if (!TryGetIdentifier(root, context.Request, out var id, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        if (!root.Value.TryGetProperty("input", out var inputElement) || inputElement.ValueKind != JsonValueKind.Object)
        {
            await Results.BadRequest(new { message = "Missing 'input' object for update operation" }).ExecuteAsync(context);
            return;
        }

        var returning = GetStringArray(root.Value, "returning");
        if (!store.TryUpdate(entityName, id, inputElement, returning, out var record))
        {
            await Results.NotFound(new { message = $"Entity '{id}' was not found" }).ExecuteAsync(context);
            return;
        }

        await Results.Json(new { data = record }, SerializerOptions, null, StatusCodes.Status200OK).ExecuteAsync(context);
    }

    private static async Task HandleDelete(HttpContext context, [FromServices] CrudRuntimeStore store)
    {
        JsonElement? root = null;
        if (context.Request.ContentLength > 0 || context.Request.Body.CanSeek && context.Request.Body.Length > 0)
        {
            root = await ParseBodyAsync(context.Request);
            if (root == null)
            {
                await Results.BadRequest(new { message = "Payload must be a JSON object" }).ExecuteAsync(context);
                return;
            }
        }

        if (!TryResolveEntity(context, root, out var entityName, out var error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        if (!TryGetIdentifier(root, context.Request, out var id, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        if (!store.TryDelete(entityName, id))
        {
            await Results.NotFound(new { message = $"Entity '{id}' was not found" }).ExecuteAsync(context);
            return;
        }

        await Results.NoContent().ExecuteAsync(context);
    }

    private static async Task<JsonElement?> ParseBodyAsync(HttpRequest request)
    {
        request.EnableBuffering();
        request.Body.Position = 0;

        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var content = await reader.ReadToEndAsync();
        request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryResolveEntity(HttpContext context, JsonElement? root, out string entityName, out IResult? error)
    {
        if (root.HasValue && root.Value.ValueKind == JsonValueKind.Object && root.Value.TryGetProperty("entity", out var entityElement) && entityElement.ValueKind == JsonValueKind.String)
        {
            var value = entityElement.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                entityName = value!;
                error = null;
                return true;
            }
        }

        if (context.Request.Query.TryGetValue("entity", out var queryValues))
        {
            var candidate = queryValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                entityName = candidate!;
                error = null;
                return true;
            }
        }

        entityName = string.Empty;
        error = Results.BadRequest(new { message = "Missing 'entity' value" });
        return false;
    }

    private static IReadOnlyCollection<string>? ResolveSelectFields(JsonElement? root, IQueryCollection query)
    {
        var fromBody = root.HasValue ? GetStringArray(root.Value, "select") : null;
        if (fromBody != null)
        {
            return fromBody;
        }

        if (!query.TryGetValue("select", out var values))
        {
            return null;
        }

        var items = new List<string>();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = part.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    items.Add(trimmed);
                }
            }
        }

        return items.Count == 0 ? null : items;
    }

    private static IReadOnlyCollection<string>? GetStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<string>();
        foreach (var element in property.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = element.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                items.Add(value!);
            }
        }

        return items.Count == 0 ? null : items;
    }

    private static bool TryGetIdentifier(JsonElement? root, HttpRequest request, out int id, out IResult? error)
    {
        if (root.HasValue && root.Value.ValueKind == JsonValueKind.Object && root.Value.TryGetProperty("key", out var keyElement) && keyElement.ValueKind == JsonValueKind.Object)
        {
            if (keyElement.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt32(out id))
            {
                error = null;
                return true;
            }

            error = Results.BadRequest(new { message = "Missing numeric 'id' inside 'key'" });
            id = default;
            return false;
        }

        if (request.Query.TryGetValue("id", out var idValues))
        {
            var candidate = idValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(candidate) && int.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
            {
                error = null;
                return true;
            }
        }

        error = Results.BadRequest(new { message = "Missing 'key' object" });
        id = default;
        return false;
    }
}

public sealed class CrudRuntimeStore
{
    private readonly ConcurrentDictionary<string, EntityStore> stores = new(StringComparer.OrdinalIgnoreCase);

    internal EntityStore GetStore(string entityName)
    {
        return stores.GetOrAdd(entityName, _ => new EntityStore());
    }

    internal IReadOnlyList<Dictionary<string, object?>> Read(string entityName, IReadOnlyCollection<string>? select)
    {
        return GetStore(entityName).Read(select);
    }

    internal bool TryUpdate(string entityName, int id, JsonElement input, IReadOnlyCollection<string>? returning, out Dictionary<string, object?> record)
    {
        return GetStore(entityName).TryUpdate(id, input, returning, out record);
    }

    internal bool TryDelete(string entityName, int id)
    {
        return GetStore(entityName).TryDelete(id);
    }
}

internal sealed class EntityStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly object gate = new();
    private readonly Dictionary<int, Dictionary<string, object?>> records = new();
    private int nextId = 1;

    public Dictionary<string, object?> Create(JsonElement input, IReadOnlyCollection<string>? returning)
    {
        lock (gate)
        {
            var record = ExtractRecord(input);
            var id = nextId++;
            record["id"] = id;
            records[id] = record;
            return ProjectRecord(record, returning);
        }
    }

    public IReadOnlyList<Dictionary<string, object?>> Read(IReadOnlyCollection<string>? select)
    {
        lock (gate)
        {
            return records.Values
                .Select(record => ProjectRecord(record, select))
                .ToList();
        }
    }

    public bool TryUpdate(int id, JsonElement input, IReadOnlyCollection<string>? returning, out Dictionary<string, object?> projection)
    {
        lock (gate)
        {
            if (!records.TryGetValue(id, out var record))
            {
                projection = default!;
                return false;
            }

            foreach (var property in input.EnumerateObject())
            {
                record[property.Name] = ConvertValue(property.Value);
            }

            projection = ProjectRecord(record, returning);
            return true;
        }
    }

    public bool TryDelete(int id)
    {
        lock (gate)
        {
            return records.Remove(id);
        }
    }

    private static Dictionary<string, object?> ExtractRecord(JsonElement input)
    {
        var record = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in input.EnumerateObject())
        {
            record[property.Name] = ConvertValue(property.Value);
        }

        return record;
    }

    private static Dictionary<string, object?> ProjectRecord(Dictionary<string, object?> record, IReadOnlyCollection<string>? fields)
    {
        if (fields == null || fields.Count == 0)
        {
            return new Dictionary<string, object?>(record, StringComparer.OrdinalIgnoreCase);
        }

        var projection = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            if (record.TryGetValue(field, out var value))
            {
                projection[field] = value;
            }
        }

        return projection;
    }

    private static object? ConvertValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => ParseNumber(element),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => JsonSerializer.Deserialize<object?>(element.GetRawText(), SerializerOptions)
        };
    }

    private static object ParseNumber(JsonElement element)
    {
        var raw = element.GetRawText();
        if (raw.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0)
        {
            return decimal.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            return intValue;
        }

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
        {
            return longValue;
        }

        return decimal.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
