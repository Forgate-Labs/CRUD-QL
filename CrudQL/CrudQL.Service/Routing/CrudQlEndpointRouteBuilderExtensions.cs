using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CrudQL.Service.Routing;

public static class CrudQlEndpointRouteBuilderExtensions
{
    private const string CrudRoute = "/crud";
    private static readonly object OkPayload = new { message = "ok" };

    public static IEndpointRouteBuilder MapCrudQl(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(CrudRoute, HandleCrudRequest);
        endpoints.MapPut(CrudRoute, HandleCrudRequest);
        endpoints.MapGet(CrudRoute, HandleCrudRequest);
        endpoints.MapDelete(CrudRoute, HandleCrudRequest);

        return endpoints;
    }

    private static IResult HandleCrudRequest()
    {
        return Results.Ok(OkPayload);
    }
}
