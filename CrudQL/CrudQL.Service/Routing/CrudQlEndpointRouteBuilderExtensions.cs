using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrudQL.Service.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

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

    private static async Task HandleCreate(HttpContext context, [FromServices] ICrudEntityRegistry registry)
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

        if (!TryResolveRegistration(context, registry, entityName, out var registration, out error))
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
        if (!CrudEntityExecutor.TryDeserializeEntity(registration, inputElement, out var entity, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        if (!CrudEntityExecutor.TryResolveDbContext(context, registration, out var dbContext, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        dbContext.Add(entity);
        await dbContext.SaveChangesAsync(context.RequestAborted);
        var projection = CrudEntityExecutor.Project(entity, returning);
        await Results.Json(new { data = projection }, SerializerOptions, null, StatusCodes.Status201Created).ExecuteAsync(context);
    }

    private static async Task HandleRead(HttpContext context, [FromServices] ICrudEntityRegistry registry)
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

        if (!TryResolveRegistration(context, registry, entityName, out var registration, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        if (!CrudEntityExecutor.TryResolveQueryable(context, registration, out var queryable, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        var select = ResolveSelectFields(root, context.Request.Query);
        var cast = Queryable.Cast<object>(queryable);
        var entities = await EntityFrameworkQueryableExtensions.ToListAsync(cast, context.RequestAborted);
        var data = entities.Select(item => CrudEntityExecutor.Project(item!, select)).ToList();
        await Results.Json(new { data }, SerializerOptions).ExecuteAsync(context);
    }

    private static async Task HandleUpdate(HttpContext context, [FromServices] ICrudEntityRegistry registry)
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

        if (!TryResolveRegistration(context, registry, entityName, out var registration, out error))
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

        var updateFields = GetStringArray(root.Value, "update");
        if (!CrudEntityExecutor.TryResolveDbContext(context, registration, out var dbContext, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        var entity = await CrudEntityExecutor.FindAsync(dbContext, registration, id, context.RequestAborted);
        if (entity == null)
        {
            await Results.NotFound(new { message = $"Entity '{id}' was not found" }).ExecuteAsync(context);
            return;
        }

        if (!CrudEntityExecutor.TryApplyInput(entity, inputElement, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        await dbContext.SaveChangesAsync(context.RequestAborted);
        var projection = CrudEntityExecutor.Project(entity, updateFields);
        await Results.Json(new { data = projection }, SerializerOptions).ExecuteAsync(context);
    }

    private static async Task HandleDelete(HttpContext context, [FromServices] ICrudEntityRegistry registry)
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

        if (!TryResolveRegistration(context, registry, entityName, out var registration, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        if (!TryGetIdentifier(root, context.Request, out var id, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        if (!CrudEntityExecutor.TryResolveDbContext(context, registration, out var dbContext, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        var entity = await CrudEntityExecutor.FindAsync(dbContext, registration, id, context.RequestAborted);
        if (entity == null)
        {
            await Results.NotFound(new { message = $"Entity '{id}' was not found" }).ExecuteAsync(context);
            return;
        }

        dbContext.Remove(entity);
        await dbContext.SaveChangesAsync(context.RequestAborted);
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

    private static bool TryResolveRegistration(HttpContext context, ICrudEntityRegistry registry, string entityName, out CrudEntityRegistration registration, out IResult? error)
    {
        if (registry.TryGetEntity(entityName, out registration))
        {
            error = null;
            return true;
        }

        error = Results.NotFound(new { message = $"Entity '{entityName}' is not registered" });
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

    private static class CrudEntityExecutor
    {
        private static readonly JsonNamingPolicy NamingPolicy = JsonNamingPolicy.CamelCase;
        private static readonly ConcurrentDictionary<Type, EntityMetadata> MetadataCache = new();
        private static readonly ConcurrentDictionary<Type, Func<DbContext, object>> DbSetAccessors = new();

        public static bool TryResolveDbContext(HttpContext context, CrudEntityRegistration registration, out DbContext dbContext, out IResult? error)
        {
            if (registration.ResolveContext == null)
            {
                error = Results.Problem($"Entity '{registration.EntityName}' is not bound to a DbContext", statusCode: StatusCodes.Status500InternalServerError);
                dbContext = default!;
                return false;
            }

            dbContext = registration.ResolveContext(context.RequestServices);
            error = null;
            return true;
        }

        public static bool TryResolveQueryable(HttpContext context, CrudEntityRegistration registration, out IQueryable queryable, out IResult? error)
        {
            if (registration.ResolveSet == null)
            {
                error = Results.Problem("Entity '{registration.EntityName}' is not bound to a DbSet", statusCode: StatusCodes.Status500InternalServerError);
                queryable = default!;
                return false;
            }

            var resolved = registration.ResolveSet(context.RequestServices);
            if (resolved is IQueryable result)
            {
                queryable = result;
                error = null;
                return true;
            }

            error = Results.Problem("Entity '{registration.EntityName}' did not resolve to an IQueryable set", statusCode: StatusCodes.Status500InternalServerError);
            queryable = default!;
            return false;
        }

        public static bool TryDeserializeEntity(CrudEntityRegistration registration, JsonElement input, out object entity, out IResult? error)
        {
            try
            {
                entity = JsonSerializer.Deserialize(input.GetRawText(), registration.ClrType, SerializerOptions)!;
            }
            catch (JsonException)
            {
                entity = default!;
                error = Results.BadRequest(new { message = "Unable to deserialize entity payload" });
                return false;
            }

            if (entity == null)
            {
                error = Results.BadRequest(new { message = "Unable to deserialize entity payload" });
                return false;
            }

            error = null;
            return true;
        }

        public static async Task<object?> FindAsync(DbContext dbContext, CrudEntityRegistration registration, int id, CancellationToken cancellationToken)
        {
            var set = GetDbSet(dbContext, registration.ClrType);
            dynamic dbSet = set;
            dynamic task = dbSet.FindAsync(new object?[] { id }, cancellationToken);
            var entity = await task;
            return (object?)entity;
        }

        public static bool TryApplyInput(object entity, JsonElement input, out IResult? error)
        {
            var metadata = GetMetadata(entity.GetType());
            foreach (var property in input.EnumerateObject())
            {
                if (!metadata.Properties.TryGetValue(property.Name, out var propertyInfo) || !propertyInfo.CanWrite)
                {
                    continue;
                }

                if (!TryConvertValue(propertyInfo.PropertyType, property.Value, out var converted))
                {
                    error = Results.BadRequest(new { message = $"Unable to convert value for '{property.Name}'" });
                    return false;
                }

                propertyInfo.SetValue(entity, converted);
            }

            error = null;
            return true;
        }

        public static Dictionary<string, object?> Project(object entity, IReadOnlyCollection<string>? fields)
        {
            var metadata = GetMetadata(entity.GetType());
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (fields == null || fields.Count == 0)
            {
                foreach (var field in metadata.DefaultFields)
                {
                    var property = metadata.Properties[field];
                    result[field] = property.GetValue(entity);
                }

                return result;
            }

            foreach (var field in fields)
            {
                if (metadata.Properties.TryGetValue(field, out var property))
                {
                    var key = metadata.ResolveFieldName(field);
                    result[key] = property.GetValue(entity);
                }
            }

            return result;
        }

        private static object GetDbSet(DbContext dbContext, Type entityType)
        {
            var accessor = DbSetAccessors.GetOrAdd(entityType, type =>
            {
                var method = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(type);
                return context => method.Invoke(context, null)!;
            });

            return accessor(dbContext);
        }

        private static EntityMetadata GetMetadata(Type type)
        {
            return MetadataCache.GetOrAdd(type, CreateMetadata);
        }

        private static EntityMetadata CreateMetadata(Type type)
        {
            var map = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            var defaults = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead)
                {
                    continue;
                }

                var camel = NamingPolicy.ConvertName(property.Name);
                map[camel] = property;
                map[property.Name] = property;
                if (seen.Add(camel))
                {
                    defaults.Add(camel);
                }
            }

            return new EntityMetadata(map, defaults);
        }

        private static bool TryConvertValue(Type targetType, JsonElement element, out object? value)
        {
            if (element.ValueKind == JsonValueKind.Null)
            {
                value = null;
                return true;
            }

            try
            {
                value = JsonSerializer.Deserialize(element.GetRawText(), targetType, SerializerOptions);
                return true;
            }
            catch (JsonException)
            {
                value = null;
                return false;
            }
        }

        private sealed class EntityMetadata
        {
            private readonly Dictionary<string, string> canonicalNames;

            public EntityMetadata(Dictionary<string, PropertyInfo> properties, List<string> defaultFields)
            {
                Properties = properties;
                DefaultFields = defaultFields;
                canonicalNames = properties.Keys.ToDictionary(key => key, key => NamingPolicy.ConvertName(Properties[key].Name), StringComparer.OrdinalIgnoreCase);
            }

            public Dictionary<string, PropertyInfo> Properties { get; }

            public IReadOnlyList<string> DefaultFields { get; }

            public string ResolveFieldName(string field)
            {
                if (canonicalNames.TryGetValue(field, out var name))
                {
                    return name;
                }

                return field;
            }
        }
    }
}
