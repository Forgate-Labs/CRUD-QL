using System;
using CrudQL.Service.Lifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrudQL.Service.Configuration;

public interface ICrudQlBuilder
{
    IServiceCollection Services { get; }

    ICrudQlBuilder AddEntity<TEntity>();

    ICrudQlBuilder AddEntity<TEntity>(Action<CrudEntityBuilder<TEntity>> configure);

    ICrudQlBuilder AddEntitiesFromDbContext<TContext>() where TContext : DbContext;

    ICrudQlBuilder OnEntityCreating(EntityLifecycleHook hook);

    ICrudQlBuilder OnEntityUpdating(EntityLifecycleHook hook);

    ICrudQlBuilder UseTenantFilter(string claimType = "tenant_id", string propertyName = "TenantId");
}
