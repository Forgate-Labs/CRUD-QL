using Microsoft.Extensions.DependencyInjection;

namespace CrudQL.Service.Configuration;

public interface ICrudQlBuilder
{
    IServiceCollection Services { get; }

    ICrudQlBuilder AddEntity<TEntity>();

    ICrudQlBuilder AddEntitiesFromDbContext<TContext>();
}
