using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
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

        if (!root.Value.TryGetProperty("condition", out var conditionElement) || conditionElement.ValueKind != JsonValueKind.Object)
        {
            await Results.BadRequest(new { message = "Missing 'condition' object for update operation" }).ExecuteAsync(context);
            return;
        }

        if (!root.Value.TryGetProperty("input", out var inputElement) || inputElement.ValueKind != JsonValueKind.Object)
        {
            await Results.BadRequest(new { message = "Missing 'input' object for update operation" }).ExecuteAsync(context);
            return;
        }

        if (!CrudEntityExecutor.TryBuildConditionPredicate(registration, conditionElement, out var predicate, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        var updateFields = GetStringArray(root.Value, "update");
        if (!CrudEntityExecutor.TryResolveDbContext(context, registration, out var dbContext, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        var matches = await CrudEntityExecutor.ExecutePredicateAsync(dbContext, registration, predicate, context.RequestAborted);
        if (matches.Count == 0)
        {
            await Results.NotFound(new { message = "No entities matched the provided condition" }).ExecuteAsync(context);
            return;
        }

        foreach (var match in matches)
        {
            if (!CrudEntityExecutor.TryApplyInput(match, inputElement, out error))
            {
                await error!.ExecuteAsync(context);
                return;
            }
        }

        await dbContext.SaveChangesAsync(context.RequestAborted);
        var projection = CrudEntityExecutor.Project(matches[0], updateFields);
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
        private static readonly MethodInfo QueryableWhereMethod = typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(method => method.Name == nameof(Queryable.Where) && method.GetParameters().Length == 2);
        private static readonly MethodInfo ToListAsyncMethodDefinition = typeof(EntityFrameworkQueryableExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(method => method.Name == nameof(EntityFrameworkQueryableExtensions.ToListAsync) && method.GetParameters().Length == 2);

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

        public static bool TryBuildConditionPredicate(CrudEntityRegistration registration, JsonElement condition, out LambdaExpression predicate, out IResult? error)
        {
            if (condition.ValueKind != JsonValueKind.Object)
            {
                predicate = default!;
                error = Results.BadRequest(new { message = "The 'condition' element must be a JSON object" });
                return false;
            }

            var metadata = GetMetadata(registration.ClrType);
            var parameter = Expression.Parameter(registration.ClrType, "entity");
            if (!TryBuildConditionExpression(metadata, parameter, condition, out var body, out error))
            {
                predicate = default!;
                return false;
            }

            predicate = Expression.Lambda(body, parameter);
            error = null;
            return true;
        }

        public static async Task<IReadOnlyList<object>> ExecutePredicateAsync(DbContext dbContext, CrudEntityRegistration registration, LambdaExpression predicate, CancellationToken cancellationToken)
        {
            var set = GetDbSet(dbContext, registration.ClrType);
            var queryable = (IQueryable)set;
            var whereExpression = Expression.Call(
                QueryableWhereMethod.MakeGenericMethod(registration.ClrType),
                queryable.Expression,
                predicate);
            var filtered = queryable.Provider.CreateQuery(whereExpression);
            var toListAsync = ToListAsyncMethodDefinition.MakeGenericMethod(registration.ClrType);
            var task = (Task?)toListAsync.Invoke(null, new object[] { filtered, cancellationToken });
            if (task == null)
            {
                return Array.Empty<object>();
            }

            await task.ConfigureAwait(false);
            var resultProperty = task.GetType().GetProperty("Result");
            if (resultProperty?.GetValue(task) is not IEnumerable enumerable)
            {
                return Array.Empty<object>();
            }

            return enumerable.Cast<object>().ToList();
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

        private static bool TryBuildConditionExpression(EntityMetadata metadata, ParameterExpression parameter, JsonElement element, out Expression expression, out IResult? error)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                expression = default!;
                error = Results.BadRequest(new { message = "Condition clauses must be JSON objects" });
                return false;
            }

            if (element.TryGetProperty("and", out var andElement) && andElement.ValueKind == JsonValueKind.Array)
            {
                Expression? combined = null;
                foreach (var item in andElement.EnumerateArray())
                {
                    if (!TryBuildConditionExpression(metadata, parameter, item, out var inner, out error))
                    {
                        expression = default!;
                        return false;
                    }

                    combined = combined == null ? inner : Expression.AndAlso(combined, inner);
                }

                if (combined == null)
                {
                    expression = default!;
                    error = Results.BadRequest(new { message = "Condition 'and' clauses require at least one predicate" });
                    return false;
                }

                expression = combined;
                error = null;
                return true;
            }

            if (element.TryGetProperty("or", out var orElement) && orElement.ValueKind == JsonValueKind.Array)
            {
                Expression? combined = null;
                foreach (var item in orElement.EnumerateArray())
                {
                    if (!TryBuildConditionExpression(metadata, parameter, item, out var inner, out error))
                    {
                        expression = default!;
                        return false;
                    }

                    combined = combined == null ? inner : Expression.OrElse(combined, inner);
                }

                if (combined == null)
                {
                    expression = default!;
                    error = Results.BadRequest(new { message = "Condition 'or' clauses require at least one predicate" });
                    return false;
                }

                expression = combined;
                error = null;
                return true;
            }

            if (!element.TryGetProperty("field", out var fieldElement) || fieldElement.ValueKind != JsonValueKind.String)
            {
                expression = default!;
                error = Results.BadRequest(new { message = "Condition predicates must define a 'field'" });
                return false;
            }

            var fieldName = fieldElement.GetString();
            if (string.IsNullOrWhiteSpace(fieldName) || !metadata.Properties.TryGetValue(fieldName!, out var property))
            {
                expression = default!;
                error = Results.BadRequest(new { message = $"Unknown field '{fieldName}' in condition" });
                return false;
            }

            if (!element.TryGetProperty("op", out var opElement) || opElement.ValueKind != JsonValueKind.String)
            {
                expression = default!;
                error = Results.BadRequest(new { message = "Condition predicates must define an 'op'" });
                return false;
            }

            var operation = opElement.GetString();
            if (!element.TryGetProperty("value", out var valueElement))
            {
                expression = default!;
                error = Results.BadRequest(new { message = "Condition predicates must provide a 'value'" });
                return false;
            }

            if (!TryConvertValue(property.PropertyType, valueElement, out var value))
            {
                expression = default!;
                error = Results.BadRequest(new { message = $"Unable to convert value for '{fieldName}'" });
                return false;
            }

            if (value == null && property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) == null)
            {
                expression = default!;
                error = Results.BadRequest(new { message = $"Field '{fieldName}' does not accept null values" });
                return false;
            }

            var member = Expression.Property(parameter, property);
            var constant = Expression.Constant(value, property.PropertyType);

            if (!TryCreateComparisonExpression(member, constant, operation!, fieldName!, out expression, out error))
            {
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryCreateComparisonExpression(Expression member, Expression constant, string operation, string fieldName, out Expression expression, out IResult? error)
        {
            operation = operation.Trim().ToLowerInvariant();
            switch (operation)
            {
                case "eq":
                    expression = Expression.Equal(member, constant);
                    error = null;
                    return true;
                case "neq":
                    expression = Expression.NotEqual(member, constant);
                    error = null;
                    return true;
                case "gt":
                    if (!SupportsComparison(member.Type))
                    {
                        expression = default!;
                        error = Results.BadRequest(new { message = $"Field '{fieldName}' does not support 'gt' comparisons" });
                        return false;
                    }

                    expression = Expression.GreaterThan(member, constant);
                    error = null;
                    return true;
                case "gte":
                    if (!SupportsComparison(member.Type))
                    {
                        expression = default!;
                        error = Results.BadRequest(new { message = $"Field '{fieldName}' does not support 'gte' comparisons" });
                        return false;
                    }

                    expression = Expression.GreaterThanOrEqual(member, constant);
                    error = null;
                    return true;
                case "lt":
                    if (!SupportsComparison(member.Type))
                    {
                        expression = default!;
                        error = Results.BadRequest(new { message = $"Field '{fieldName}' does not support 'lt' comparisons" });
                        return false;
                    }

                    expression = Expression.LessThan(member, constant);
                    error = null;
                    return true;
                case "lte":
                    if (!SupportsComparison(member.Type))
                    {
                        expression = default!;
                        error = Results.BadRequest(new { message = $"Field '{fieldName}' does not support 'lte' comparisons" });
                        return false;
                    }

                    expression = Expression.LessThanOrEqual(member, constant);
                    error = null;
                    return true;
                case "contains":
                    if (member.Type != typeof(string))
                    {
                        expression = default!;
                        error = Results.BadRequest(new { message = $"Field '{fieldName}' does not support 'contains' comparisons" });
                        return false;
                    }

                    var method = typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) });
                    expression = Expression.Call(member, method!, constant);
                    error = null;
                    return true;
                default:
                    expression = default!;
                    error = Results.BadRequest(new { message = $"Unsupported operation '{operation}' in condition" });
                    return false;
            }
        }

        private static bool SupportsComparison(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return typeof(IComparable).IsAssignableFrom(type);
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
