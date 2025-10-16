using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using CrudQL.Service.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrudQL.Service.Configuration;

public class CrudQlBuilder : ICrudQlBuilder
{
    private readonly ICrudEntityRegistry entityRegistry;
    private Type? dbContextType;

    public CrudQlBuilder(IServiceCollection services)
    {
        Services = services;
        entityRegistry = EnsureRegistry(services);
    }

    public IServiceCollection Services { get; }

    public ICrudQlBuilder AddEntity<TEntity>()
    {
        entityRegistry.RegisterEntity(typeof(TEntity));

        if (dbContextType != null)
        {
            RegisterResolver(typeof(TEntity));
        }

        return this;
    }

    public ICrudQlBuilder AddEntitiesFromDbContext<TContext>()
        where TContext : DbContext
    {
        dbContextType = typeof(TContext);

        foreach (var registration in entityRegistry.Entities.ToArray())
        {
            RegisterResolver(registration.ClrType);
        }

        return this;
    }

    private void RegisterResolver(Type entityType)
    {
        if (dbContextType == null)
        {
            return;
        }

        entityRegistry.RegisterEntitySetResolver(entityType, provider =>
        {
            var dbContext = (DbContext)provider.GetRequiredService(dbContextType);
            return ResolveDbSet(entityType, dbContext);
        });
    }

    private static object ResolveDbSet(Type entityType, DbContext dbContext)
    {
        var accessor = DbContextSetAccessors.GetOrAdd(entityType, CreateAccessor);
        return accessor(dbContext);
    }

    private static readonly ConcurrentDictionary<Type, Func<DbContext, object>> DbContextSetAccessors = new();

    private static Func<DbContext, object> CreateAccessor(Type entityType)
    {
        var setMethod = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(entityType);
        return context => setMethod.Invoke(context, null)!;
    }

    private static ICrudEntityRegistry EnsureRegistry(IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICrudEntityRegistry));
        if (descriptor?.ImplementationInstance is ICrudEntityRegistry existing)
        {
            return existing;
        }

        if (descriptor != null)
        {
            services.Remove(descriptor);
        }

        var registry = new CrudEntityRegistry();
        services.AddSingleton<ICrudEntityRegistry>(registry);
        return registry;
    }
}
