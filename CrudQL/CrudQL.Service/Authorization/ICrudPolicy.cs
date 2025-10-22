using System.Security.Claims;

namespace CrudQL.Service.Authorization;

public interface ICrudPolicy
{
    bool IsAuthorized(ClaimsPrincipal user, CrudAction action);
}

public interface ICrudPolicy<in TEntity> : ICrudPolicy
{
}
