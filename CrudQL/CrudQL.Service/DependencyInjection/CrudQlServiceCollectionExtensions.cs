using CrudQL.Service.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CrudQL.Service.DependencyInjection;

public static class CrudQlServiceCollectionExtensions
{
    public static ICrudQlBuilder AddCrudQl(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return new CrudQlBuilder(services);
    }
}
