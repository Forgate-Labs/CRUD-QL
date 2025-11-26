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
using System.Security.Claims;
using CrudQL.Service.Authorization;
using CrudQL.Service.Entities;
using CrudQL.Service.Pagination;
using CrudQL.Service.Validation;
using FluentValidation;
using FluentValidation.Results;
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

        endpoints.MapPost(CrudRoute, HandleCreate).WithCrudQlDocumentation(CrudAction.Create);
        endpoints.MapGet(CrudRoute, HandleRead).WithCrudQlDocumentation(CrudAction.Read);
        endpoints.MapPut(CrudRoute, HandleUpdate).WithCrudQlDocumentation(CrudAction.Update);
        endpoints.MapDelete(CrudRoute, HandleDelete).WithCrudQlDocumentation(CrudAction.Delete);

        return endpoints;
    }

    private static async Task HandleCreate(HttpContext context, [FromServices] ICrudEntityRegistry registry)
    {
        var body = await ParseBodyAsync(context.Request);
        if (!body.IsValidJson)
        {
            await Results.BadRequest(new { message = "Payload must be a JSON object" }).ExecuteAsync(context);
            return;
        }

        var root = body.Root;
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

        if (!CrudEntityExecutor.TryAuthorize(context, registration, CrudAction.Create, out error))
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

        if (!CrudEntityExecutor.TryValidate(registration, CrudAction.Create, entity!, out error))
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
        var projectionContext = CrudEntityExecutor.ResolveProjection(context, registration, CrudAction.Create);
        var projection = CrudEntityExecutor.Project(entity, returning, projectionContext);
        await Results.Json(new { data = projection }, SerializerOptions, null, StatusCodes.Status201Created).ExecuteAsync(context);
    }

    private static async Task HandleRead(HttpContext context, [FromServices] ICrudEntityRegistry registry)
    {
        if (!TryResolveEntity(context, null, out var entityName, out var error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        if (!TryResolveRegistration(context, registry, entityName, out var registration, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        if (!CrudEntityExecutor.TryAuthorize(context, registration, CrudAction.Read, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        JsonElement? filterElement = null;
        if (context.Request.Query.TryGetValue("filter", out var filterValues))
        {
            var filterString = filterValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(filterString))
            {
                try
                {
                    using var document = JsonDocument.Parse(filterString);
                    filterElement = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    await Results.BadRequest(new { message = "Invalid JSON in 'filter' query parameter" }).ExecuteAsync(context);
                    return;
                }
            }
        }
        var filterContext = new CrudFilterContext(entityName, registration.ClrType, filterElement);
        if (!CrudEntityExecutor.TryValidate(registration, CrudAction.Read, filterContext, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        if (!CrudEntityExecutor.TryResolveQueryable(context, registration, out var queryable, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        var softDeleteRule = CrudEntityExecutor.ResolveSoftDeleteRule(registration);
        if (softDeleteRule != null)
        {
            queryable = CrudEntityExecutor.ApplySoftDeleteFilter(queryable, registration.ClrType, softDeleteRule);
        }

        if (!TryResolveSelect(null, context.Request.Query, out var select, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        var user = context.User ?? new ClaimsPrincipal(new ClaimsIdentity());
        if (!CrudEntityExecutor.TryValidateSelect(user, registration, select, out var includePaths, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        if (includePaths.Count > 0)
        {
            queryable = CrudEntityExecutor.ApplyIncludes(queryable, registration.ClrType, includePaths);
        }

        if (!TryParsePaginationRequest(context, registration, out var paginationRequest, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        PaginationResponse? paginationResponse = null;
        if (paginationRequest != null)
        {
            var paginationConfig = registration.PaginationConfig ?? new PaginationConfig();
            var pageSize = paginationRequest.PageSize;
            var page = paginationRequest.Page;
            var skip = (page - 1) * pageSize;

            int? totalRecords = null;
            if (paginationRequest.IncludeCount)
            {
                totalRecords = await EntityFrameworkQueryableExtensions.CountAsync(queryable, context.RequestAborted);
            }

            queryable = Queryable.Skip(queryable, skip);
            queryable = Queryable.Take(queryable, pageSize);

            var cast = Queryable.Cast<object>(queryable);
            var entities = await EntityFrameworkQueryableExtensions.ToListAsync(cast, context.RequestAborted);
            var projectionContext = CrudEntityExecutor.ResolveProjection(context, registration, CrudAction.Read);
            var data = entities.Select(item => CrudEntityExecutor.Project(item!, select, projectionContext)).ToList();

            var hasNextPage = totalRecords.HasValue
                ? skip + pageSize < totalRecords.Value
                : data.Count == pageSize;
            var hasPreviousPage = page > 1;
            var totalPages = totalRecords.HasValue ? (int)Math.Ceiling((double)totalRecords.Value / pageSize) : (int?)null;

            paginationResponse = new PaginationResponse(
                page,
                pageSize,
                hasNextPage,
                hasPreviousPage,
                totalRecords,
                totalPages);

            await Results.Json(new { data, pagination = paginationResponse }, SerializerOptions).ExecuteAsync(context);
        }
        else
        {
            var cast = Queryable.Cast<object>(queryable);
            var entities = await EntityFrameworkQueryableExtensions.ToListAsync(cast, context.RequestAborted);
            var projectionContext = CrudEntityExecutor.ResolveProjection(context, registration, CrudAction.Read);
            var data = entities.Select(item => CrudEntityExecutor.Project(item!, select, projectionContext)).ToList();
            await Results.Json(new { data }, SerializerOptions).ExecuteAsync(context);
        }
    }

    private static async Task HandleUpdate(HttpContext context, [FromServices] ICrudEntityRegistry registry)
    {
        var body = await ParseBodyAsync(context.Request);
        if (!body.IsValidJson)
        {
            await Results.BadRequest(new { message = "Payload must be a JSON object" }).ExecuteAsync(context);
            return;
        }

        var root = body.Root;
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

        if (!CrudEntityExecutor.TryAuthorize(context, registration, CrudAction.Update, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        if (!root.Value.TryGetProperty("condition", out var conditionElement) || conditionElement.ValueKind != JsonValueKind.Object)
        {
            await Results.BadRequest(new { message = "Missing 'condition' object for update operation" }).ExecuteAsync(context);
            return;
        }

        if (!root.Value.TryGetProperty("update", out var updateElement) || updateElement.ValueKind != JsonValueKind.Object)
        {
            await Results.BadRequest(new { message = "Missing 'update' object for update operation" }).ExecuteAsync(context);
            return;
        }

        if (!CrudEntityExecutor.TryBuildConditionPredicate(registration, conditionElement, out var predicate, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

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
            if (!CrudEntityExecutor.TryApplyUpdate(match, updateElement, out error))
            {
                await error!.ExecuteAsync(context);
                return;
            }

            if (!CrudEntityExecutor.TryValidate(registration, CrudAction.Update, match, out error))
            {
                await error!.ExecuteAsync(context);
                return;
            }
        }

        await dbContext.SaveChangesAsync(context.RequestAborted);
        var affectedRows = matches.Count;
        await Results.Json(new { affectedRows }, SerializerOptions).ExecuteAsync(context);
    }

    private static async Task HandleDelete(HttpContext context, [FromServices] ICrudEntityRegistry registry)
    {
        JsonElement? root = null;
        if (context.Request.ContentLength > 0 || context.Request.Body.CanSeek && context.Request.Body.Length > 0)
        {
            var body = await ParseBodyAsync(context.Request);
            if (!body.IsValidJson)
            {
                await Results.BadRequest(new { message = "Payload must be a JSON object" }).ExecuteAsync(context);
                return;
            }

            root = body.Root;
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

        if (!CrudEntityExecutor.TryAuthorize(context, registration, CrudAction.Delete, out error))
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

        if (!CrudEntityExecutor.TryValidate(registration, CrudAction.Delete, entity, out error))
        {
            await error!.ExecuteAsync(context);
            return;
        }

        var softDeleteRule = CrudEntityExecutor.ResolveSoftDeleteRule(registration);
        if (softDeleteRule != null)
        {
            if (CrudEntityExecutor.IsSoftDeleted(entity, softDeleteRule))
            {
                await Results.BadRequest(new { message = "Entity is already deleted" }).ExecuteAsync(context);
                return;
            }

            CrudEntityExecutor.ApplySoftDelete(entity, softDeleteRule);
            await dbContext.SaveChangesAsync(context.RequestAborted);
            await Results.NoContent().ExecuteAsync(context);
            return;
        }

        dbContext.Remove(entity);
        await dbContext.SaveChangesAsync(context.RequestAborted);
        await Results.NoContent().ExecuteAsync(context);
    }

    private static async Task<BodyParseResult> ParseBodyAsync(HttpRequest request)
    {
        request.EnableBuffering();
        request.Body.Position = 0;

        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var content = await reader.ReadToEndAsync();
        request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(content))
        {
            return new BodyParseResult(null, false, true);
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            return new BodyParseResult(document.RootElement.Clone(), true, true);
        }
        catch (JsonException)
        {
            return new BodyParseResult(null, true, false);
        }
    }

    private readonly record struct BodyParseResult(JsonElement? Root, bool HasContent, bool IsValidJson);

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

    private static bool TryResolveSelect(JsonElement? root, IQueryCollection query, out CrudEntityExecutor.SelectNode? select, out IResult? error)
    {
        if (!query.TryGetValue("select", out var values))
        {
            select = null;
            error = null;
            return true;
        }

        var selectString = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(selectString))
        {
            select = null;
            error = null;
            return true;
        }

        if (selectString.TrimStart().StartsWith("["))
        {
            try
            {
                using var document = JsonDocument.Parse(selectString);
                var selectElement = document.RootElement.Clone();
                if (selectElement.ValueKind != JsonValueKind.Array)
                {
                    error = Results.BadRequest(new { message = "The 'select' element must be an array" });
                    select = null;
                    return false;
                }

                if (!CrudEntityExecutor.TryParseSelectArray(selectElement, out var parsed, out error))
                {
                    select = null;
                    return false;
                }

                select = parsed.Fields == null && parsed.Includes.Count == 0 ? null : parsed;
                return true;
            }
            catch (JsonException)
            {
                error = Results.BadRequest(new { message = "Invalid JSON in 'select' query parameter" });
                select = null;
                return false;
            }
        }

        var fields = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = part.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                if (seen.Add(trimmed))
                {
                    fields.Add(trimmed);
                }
            }
        }

        select = fields.Count == 0
            ? null
            : new CrudEntityExecutor.SelectNode(fields, CrudEntityExecutor.CreateEmptyIncludeMap());
        error = null;
        return true;
    }

    private static bool TryParsePaginationRequest(HttpContext context, CrudEntityRegistration registration, out PaginationRequest? paginationRequest, out IResult? error)
    {
        var query = context.Request.Query;
        paginationRequest = null;
        error = null;

        var hasPage = query.TryGetValue("page", out var pageValues);
        var hasPageSize = query.TryGetValue("pageSize", out var pageSizeValues);
        var hasIncludeCount = query.TryGetValue("includeCount", out var includeCountValues);

        if (!hasPage && !hasPageSize)
        {
            return true;
        }

        if (!hasPage)
        {
            error = Results.BadRequest(new { message = "When using pagination, 'page' parameter is required" });
            return false;
        }

        if (!int.TryParse(pageValues.FirstOrDefault(), out var page) || page < 1)
        {
            error = Results.BadRequest(new { message = "Page must be greater than or equal to 1" });
            return false;
        }

        var paginationConfig = registration.PaginationConfig ?? new PaginationConfig();
        var pageSize = paginationConfig.DefaultPageSize;

        if (hasPageSize)
        {
            if (!int.TryParse(pageSizeValues.FirstOrDefault(), out pageSize) || pageSize < 1)
            {
                error = Results.BadRequest(new { message = "PageSize must be greater than or equal to 1" });
                return false;
            }

            if (pageSize > paginationConfig.MaxPageSize)
            {
                error = Results.BadRequest(new { message = $"PageSize cannot exceed {paginationConfig.MaxPageSize}" });
                return false;
            }
        }

        var includeCount = false;
        if (hasIncludeCount)
        {
            if (bool.TryParse(includeCountValues.FirstOrDefault(), out var parsedIncludeCount))
            {
                includeCount = parsedIncludeCount;
            }
        }

        paginationRequest = new PaginationRequest(page, pageSize, includeCount);
        return true;
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
        private static readonly MethodInfo IncludeStringMethod = typeof(EntityFrameworkQueryableExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(method => method.Name == nameof(EntityFrameworkQueryableExtensions.Include) && method.GetParameters().Length == 2 && method.GetParameters()[1].ParameterType == typeof(string));
        private static readonly MethodInfo ToListAsyncMethodDefinition = typeof(EntityFrameworkQueryableExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(method => method.Name == nameof(EntityFrameworkQueryableExtensions.ToListAsync) && method.GetParameters().Length == 2);
        private static readonly ConcurrentDictionary<Type, Func<object, IValidationContext>> ValidationContextFactories = new();

        public static bool TryValidate(CrudEntityRegistration registration, CrudAction action, object entity, out IResult? error)
        {
            if (!registration.Validators.TryGetValue(action, out var registrations) || registrations.Count == 0)
            {
                error = null;
                return true;
            }

            var failures = new List<ValidationFailure>();
            foreach (var validatorRegistration in registrations)
            {
                var result = InvokeValidator(validatorRegistration.TargetType, validatorRegistration.Validator, entity);
                if (!result.IsValid)
                {
                    failures.AddRange(result.Errors);
                }
            }

            if (failures.Count == 0)
            {
                error = null;
                return true;
            }

            var errors = failures
                .GroupBy(failure => failure.PropertyName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Where(f => !string.IsNullOrWhiteSpace(f.ErrorMessage)).Select(f => f.ErrorMessage).Distinct().ToArray(),
                    StringComparer.OrdinalIgnoreCase);

            error = Results.Json(new { message = "Validation failed", errors }, SerializerOptions, null, StatusCodes.Status400BadRequest);
            return false;
        }

        public static bool TryAuthorize(HttpContext context, CrudEntityRegistration registration, CrudAction action, out IResult? error)
        {
            var policy = registration.Policy;
            if (policy == null)
            {
                error = null;
                return true;
            }

            var user = context.User ?? new ClaimsPrincipal();
            if (policy.IsAuthorized(user, action))
            {
                error = null;
                return true;
            }

            error = Results.Json(
                new { message = $"User is not authorized to perform {action} on {registration.EntityName}" },
                SerializerOptions,
                null,
                StatusCodes.Status401Unauthorized);
            return false;
        }

        public static CrudSoftDeleteRule? ResolveSoftDeleteRule(CrudEntityRegistration registration)
        {
            if (registration.Policy is not ISoftDeletePolicy softDeletePolicy)
            {
                return null;
            }

            return softDeletePolicy.ResolveSoftDelete(CrudAction.Delete);
        }

        public static IQueryable ApplySoftDeleteFilter(IQueryable queryable, Type entityType, CrudSoftDeleteRule rule)
        {
            var parameter = Expression.Parameter(entityType, "entity");
            var property = Expression.Property(parameter, rule.FlagProperty);
            var condition = Expression.Equal(property, Expression.Constant(false));
            var lambda = Expression.Lambda(condition, parameter);
            var whereExpression = Expression.Call(
                QueryableWhereMethod.MakeGenericMethod(entityType),
                queryable.Expression,
                lambda);
            return queryable.Provider.CreateQuery(whereExpression);
        }

        public static bool IsSoftDeleted(object entity, CrudSoftDeleteRule rule)
        {
            var value = rule.FlagProperty.GetValue(entity);
            if (value is bool flag)
            {
                return flag;
            }

            return false;
        }

        public static void ApplySoftDelete(object entity, CrudSoftDeleteRule rule)
        {
            rule.FlagProperty.SetValue(entity, true);
            if (rule.TimestampProperty == null)
            {
                return;
            }

            var now = rule.UseUtc ? DateTime.UtcNow : DateTime.Now;
            if (rule.TimestampProperty.PropertyType == typeof(DateTime))
            {
                rule.TimestampProperty.SetValue(entity, now);
                return;
            }

            rule.TimestampProperty.SetValue(entity, (DateTime?)now);
        }

        private static ValidationResult InvokeValidator(Type entityType, IValidator validator, object instance)
        {
            if (!entityType.IsInstanceOfType(instance))
            {
                throw new InvalidOperationException($"Instance is not of type '{entityType.Name}'");
            }

            var validateMethod = validator.GetType().GetMethod("Validate", BindingFlags.Public | BindingFlags.Instance, null, new[] { entityType }, null);
            if (validateMethod != null)
            {
                return (ValidationResult)validateMethod.Invoke(validator, new[] { instance })!;
            }

            var context = CreateValidationContext(entityType, instance);
            return validator.Validate(context);
        }

        private static IValidationContext CreateValidationContext(Type entityType, object instance)
        {
            var factory = ValidationContextFactories.GetOrAdd(entityType, CreateValidationContextFactory);
            return factory(instance);
        }

        private static Func<object, IValidationContext> CreateValidationContextFactory(Type entityType)
        {
            var contextType = typeof(ValidationContext<>).MakeGenericType(entityType);
            var constructor = contextType.GetConstructor(new[] { entityType })
                ?? throw new InvalidOperationException($"ValidationContext constructor for '{entityType.Name}' was not found.");

            return instance => (IValidationContext)constructor.Invoke(new[] { instance });
        }

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
            var metadata = GetMetadata(registration.ClrType);
            var unknownFields = ResolveUnknownFields(metadata, input);
            if (unknownFields.Count > 0)
            {
                entity = default!;
                error = Results.Json(
                    new { message = "Unknown fields in payload", entity = registration.EntityName, fields = unknownFields },
                    SerializerOptions,
                    null,
                    StatusCodes.Status400BadRequest);
                return false;
            }

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

        public static bool TryValidateSelect(
            ClaimsPrincipal user,
            CrudEntityRegistration registration,
            SelectNode? select,
            out IReadOnlyCollection<string> includePaths,
            out IResult? error)
        {
            if (select == null)
            {
                includePaths = Array.Empty<string>();
                error = null;
                return true;
            }

            var userRoles = ResolveUserRoles(user);
            var includeList = new List<string>();
            var includeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!ValidateSelectNode(
                    registration.ClrType,
                    registration.EntityName,
                    select,
                    registration.Includes,
                    userRoles,
                    null,
                    null,
                    includeList,
                    includeSet,
                    out error))
            {
                includePaths = Array.Empty<string>();
                return false;
            }

            includePaths = includeList;
            error = null;
            return true;
        }

        public static bool TryParseSelectArray(JsonElement element, out SelectNode select, out IResult? error)
        {
            var builder = new SelectNodeBuilder();
            if (!builder.TryRead(element, out error))
            {
                select = default!;
                return false;
            }

            select = builder.Build();
            return true;
        }

        public static IReadOnlyDictionary<string, SelectNode> CreateEmptyIncludeMap()
        {
            return new Dictionary<string, SelectNode>(StringComparer.OrdinalIgnoreCase);
        }

        public static IQueryable ApplyIncludes(IQueryable queryable, Type entityType, IReadOnlyCollection<string> includePaths)
        {
            if (includePaths.Count == 0)
            {
                return queryable;
            }

            var method = IncludeStringMethod.MakeGenericMethod(entityType);
            foreach (var path in includePaths)
            {
                queryable = (IQueryable)method.Invoke(null, new object[] { queryable, path })!;
            }

            return queryable;
        }

        private static bool ValidateSelectNode(
            Type entityType,
            string entityName,
            SelectNode node,
            IReadOnlyDictionary<string, CrudEntityIncludeRegistration> allowedIncludes,
            HashSet<string> userRoles,
            string? requestedParentPath,
            string? canonicalParentPath,
            List<string> includePaths,
            HashSet<string> includeSet,
            out IResult? error)
        {
            var metadata = GetMetadata(entityType);
            if (node.Fields != null)
            {
                var unknown = new List<string>();
                foreach (var field in node.Fields)
                {
                    if (!metadata.Properties.ContainsKey(field))
                    {
                        unknown.Add(field);
                    }
                }

                if (unknown.Count > 0)
                {
                    error = Results.Json(
                        new { message = "Unknown fields in payload", entity = entityName, fields = unknown },
                        SerializerOptions,
                        null,
                        StatusCodes.Status400BadRequest);
                    return false;
                }
            }

            foreach (var include in node.Includes)
            {
                var requestedSegment = include.Key;
                if (!metadata.Properties.TryGetValue(requestedSegment, out var property))
                {
                    error = Results.Json(
                        new { message = "Unknown fields in payload", entity = entityName, fields = new[] { requestedSegment } },
                        SerializerOptions,
                        null,
                        StatusCodes.Status400BadRequest);
                    return false;
                }

                var canonicalSegment = property.Name;
                if (!allowedIncludes.TryGetValue(requestedSegment, out var includeRegistration))
                {
                    var requestedPath = BuildIncludePath(requestedParentPath, requestedSegment);
                    error = Results.Json(
                        new { message = $"Include '{requestedPath}' is not allowed for the current user" },
                        SerializerOptions,
                        null,
                        StatusCodes.Status422UnprocessableEntity);
                    return false;
                }

                if (!IsNavigationProperty(property.PropertyType))
                {
                    var requestedPath = BuildIncludePath(requestedParentPath, requestedSegment);
                    error = Results.Json(
                        new { message = $"Include '{requestedPath}' targets a non-navigation property" },
                        SerializerOptions,
                        null,
                        StatusCodes.Status400BadRequest);
                    return false;
                }

                if (includeRegistration.Roles != null &&
                    includeRegistration.Roles.Count > 0 &&
                    !HasRequiredRole(userRoles, includeRegistration.Roles))
                {
                    var requestedPath = BuildIncludePath(requestedParentPath, requestedSegment);
                    error = Results.Json(
                        new { message = $"Include '{requestedPath}' is not allowed for the current user" },
                        SerializerOptions,
                        null,
                        StatusCodes.Status422UnprocessableEntity);
                    return false;
                }

                var canonicalPath = BuildIncludePath(canonicalParentPath, canonicalSegment);
                if (includeSet.Add(canonicalPath))
                {
                    includePaths.Add(canonicalPath);
                }

                var requestedPathForChildren = BuildIncludePath(requestedParentPath, requestedSegment);
                var childType = ResolveNavigationType(property.PropertyType);
                var childEntityName = childType.Name;
                if (!ValidateSelectNode(
                        childType,
                        childEntityName,
                        include.Value,
                        includeRegistration.Children,
                        userRoles,
                        requestedPathForChildren,
                        canonicalPath,
                        includePaths,
                        includeSet,
                        out error))
                {
                    return false;
                }
            }

            error = null;
            return true;
        }

        private static HashSet<string> ResolveUserRoles(ClaimsPrincipal user)
        {
            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (user == null)
            {
                return roles;
            }

            foreach (var claim in user.FindAll(ClaimTypes.Role))
            {
                if (!string.IsNullOrWhiteSpace(claim.Value))
                {
                    roles.Add(claim.Value);
                }
            }

            return roles;
        }

        private static bool HasRequiredRole(HashSet<string> userRoles, IReadOnlyCollection<string> requiredRoles)
        {
            if (userRoles.Count == 0)
            {
                return false;
            }

            foreach (var role in requiredRoles)
            {
                if (userRoles.Contains(role))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildIncludePath(string? parent, string segment)
        {
            return string.IsNullOrEmpty(parent) ? segment : $"{parent}.{segment}";
        }

        private static bool IsNavigationProperty(Type propertyType)
        {
            if (propertyType == typeof(string))
            {
                return false;
            }

            if (propertyType.IsArray)
            {
                var elementType = propertyType.GetElementType();
                if (elementType == typeof(byte))
                {
                    return false;
                }

                return elementType != null && !elementType.IsValueType && elementType != typeof(char);
            }

            if (typeof(IEnumerable).IsAssignableFrom(propertyType) && propertyType != typeof(string))
            {
                var elementType = ResolveNavigationType(propertyType);
                return !elementType.IsValueType && elementType != typeof(string);
            }

            return !propertyType.IsValueType;
        }

        private static Type ResolveNavigationType(Type propertyType)
        {
            if (propertyType.IsArray)
            {
                return propertyType.GetElementType() ?? propertyType;
            }

            if (propertyType.IsGenericType)
            {
                var argument = propertyType.GenericTypeArguments.FirstOrDefault();
                if (argument != null)
                {
                    return argument;
                }
            }

            var enumerableInterface = propertyType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            if (enumerableInterface != null)
            {
                return enumerableInterface.GenericTypeArguments[0];
            }

            return propertyType;
        }

        private sealed class SelectNodeBuilder
        {
            private readonly HashSet<string> fields = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, SelectNodeBuilder> includes = new(StringComparer.OrdinalIgnoreCase);

            public bool TryRead(JsonElement element, out IResult? error)
            {
                foreach (var item in element.EnumerateArray())
                {
                    switch (item.ValueKind)
                    {
                        case JsonValueKind.String:
                            var field = item.GetString();
                            if (!string.IsNullOrWhiteSpace(field))
                            {
                                fields.Add(field);
                            }

                            break;
                        case JsonValueKind.Object:
                            foreach (var property in item.EnumerateObject())
                            {
                                if (property.Value.ValueKind != JsonValueKind.Array)
                                {
                                    error = Results.BadRequest(new { message = $"Select include '{property.Name}' must be an array" });
                                    return false;
                                }

                                var child = GetOrAddChild(property.Name);
                                if (!child.TryRead(property.Value, out error))
                                {
                                    return false;
                                }
                            }

                            break;
                        default:
                            error = Results.BadRequest(new { message = "Select entries must be strings or objects" });
                            return false;
                    }
                }

                error = null;
                return true;
            }

            public SelectNode Build()
            {
                IReadOnlyCollection<string>? builtFields = fields.Count > 0 ? fields.ToArray() : null;
                var builtIncludes = new Dictionary<string, SelectNode>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in includes)
                {
                    builtIncludes[pair.Key] = pair.Value.Build();
                }

                return new SelectNode(builtFields, builtIncludes);
            }

            private SelectNodeBuilder GetOrAddChild(string name)
            {
                if (!includes.TryGetValue(name, out var child))
                {
                    child = new SelectNodeBuilder();
                    includes[name] = child;
                }

                return child;
            }
        }

        public sealed class SelectNode
        {
            public SelectNode(IReadOnlyCollection<string>? fields, IReadOnlyDictionary<string, SelectNode> includes)
            {
                Fields = fields;
                Includes = includes;
            }

            public IReadOnlyCollection<string>? Fields { get; }

            public IReadOnlyDictionary<string, SelectNode> Includes { get; }
        }

        private static List<string> ResolveUnknownFields(EntityMetadata metadata, JsonElement input)
        {
            var unknown = new List<string>();
            if (input.ValueKind != JsonValueKind.Object)
            {
                return unknown;
            }

            foreach (var property in input.EnumerateObject())
            {
                if (!metadata.Properties.ContainsKey(property.Name))
                {
                    unknown.Add(property.Name);
                }
            }

            return unknown;
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

        public static bool TryApplyUpdate(object entity, JsonElement update, out IResult? error)
        {
            var metadata = GetMetadata(entity.GetType());
            foreach (var property in update.EnumerateObject())
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

        public static Dictionary<string, object?> Project(object entity, IReadOnlyCollection<string>? fields, ProjectionContext? projection)
        {
            SelectNode? select = null;
            if (fields != null && fields.Count > 0)
            {
                select = new SelectNode(fields, CreateEmptyIncludeMap());
            }

            return Project(entity, select, projection);
        }

        public static Dictionary<string, object?> Project(object entity, SelectNode? select, ProjectionContext? projection)
        {
            var metadata = GetMetadata(entity.GetType());
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var fieldSet = select?.Fields ?? metadata.DefaultFields;

            foreach (var field in fieldSet)
            {
                if (!metadata.Properties.TryGetValue(field, out var property))
                {
                    continue;
                }

                var key = metadata.ResolveFieldName(field);
                var value = property.GetValue(entity);
                if (projection != null && projection.ShouldMask(key))
                {
                    result[key] = projection.SuppressionValue;
                    continue;
                }

                result[key] = value;
            }

            if (select?.Includes != null)
            {
                foreach (var include in select.Includes)
                {
                    if (!metadata.Properties.TryGetValue(include.Key, out var property))
                    {
                        continue;
                    }

                    var key = metadata.ResolveFieldName(include.Key);
                    var value = property.GetValue(entity);
                    if (value == null)
                    {
                        result[key] = null;
                        continue;
                    }

                    if (value is IEnumerable enumerable && value is not string)
                    {
                        var items = new List<object?>();
                        foreach (var item in enumerable)
                        {
                            if (item == null)
                            {
                                items.Add(null);
                                continue;
                            }

                            items.Add(Project(item, include.Value, null));
                        }

                        result[key] = items;
                        continue;
                    }

                    result[key] = Project(value, include.Value, null);
                }
            }

            return result;
        }

        public static ProjectionContext? ResolveProjection(HttpContext context, CrudEntityRegistration registration, CrudAction action)
        {
            var policy = registration.Policy as ICrudProjectionPolicy;
            if (policy == null)
            {
                return null;
            }

            var user = context.User ?? new ClaimsPrincipal(new ClaimsIdentity());
            var rule = policy.ResolveProjection(user, action);
            if (rule == null || rule.Fields.Count == 0)
            {
                return null;
            }

            return new ProjectionContext(rule.Fields, rule.SuppressionValue);
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

        internal sealed class ProjectionContext
        {
            private readonly HashSet<string> allowedFields;

            public ProjectionContext(IReadOnlyCollection<string> fields, string suppressionValue)
            {
                allowedFields = new HashSet<string>(fields, StringComparer.OrdinalIgnoreCase);
                SuppressionValue = suppressionValue;
            }

            public string SuppressionValue { get; }

            public bool ShouldMask(string field)
            {
                if (allowedFields.Count == 0)
                {
                    return false;
                }

                return !allowedFields.Contains(field);
            }
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
