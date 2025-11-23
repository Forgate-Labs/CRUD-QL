using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;

namespace CrudQL.Service.Authorization;

public interface ICrudPolicy
{
    bool IsAuthorized(ClaimsPrincipal user, CrudAction action);
}

public interface ICrudPolicy<in TEntity> : ICrudPolicy
{
}

public interface ICrudProjectionPolicy
{
    CrudProjectionRule? ResolveProjection(ClaimsPrincipal user, CrudAction action);
}

public sealed record CrudProjectionRule(IReadOnlyCollection<string> Fields, string SuppressionValue);

public interface ISoftDeletePolicy
{
    CrudSoftDeleteRule? ResolveSoftDelete(CrudAction action);
}

public sealed record CrudSoftDeleteRule(PropertyInfo FlagProperty, PropertyInfo? TimestampProperty, bool UseUtc);
