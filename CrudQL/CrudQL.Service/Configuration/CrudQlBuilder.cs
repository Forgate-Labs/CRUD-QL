using Microsoft.Extensions.DependencyInjection;

namespace CrudQL.Service.Configuration;

public class CrudQlBuilder : ICrudQlBuilder
{
    public CrudQlBuilder(IServiceCollection services)
    {
        Services = services;
    }

    public IServiceCollection Services { get; }

    public ICrudQlBuilder AddEntity<TEntity>()
    {
        return this;
    }

    public ICrudQlBuilder AddEntitiesFromDbContext<TContext>()
    {
        return this;
    }
}
