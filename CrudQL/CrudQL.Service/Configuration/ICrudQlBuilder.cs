using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrudQL.Service.Configuration;

public interface ICrudQlBuilder
{
    IServiceCollection Services { get; }

    ICrudQlBuilder AddEntity<TEntity>();

    ICrudQlBuilder AddEntity<TEntity>(Action<CrudEntityBuilder<TEntity>> configure);

    ICrudQlBuilder AddEntitiesFromDbContext<TContext>() where TContext : DbContext;
}
