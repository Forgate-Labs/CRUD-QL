using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace CrudQL.Service.Entities;

internal sealed class CrudEntityRegistry : ICrudEntityRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<Type, CrudEntityRegistration> registrations = new();

    public IReadOnlyCollection<CrudEntityRegistration> Entities
    {
        get
        {
            lock (gate)
            {
                return registrations.Values.ToList();
            }
        }
    }

    public void RegisterEntity(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        lock (gate)
        {
            if (!registrations.ContainsKey(entityType))
            {
                registrations[entityType] = new CrudEntityRegistration(entityType.Name, entityType);
            }
        }
    }

    public void RegisterEntitySetResolver(Type entityType, Func<IServiceProvider, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(resolver);

        lock (gate)
        {
            if (registrations.TryGetValue(entityType, out var existing))
            {
                registrations[entityType] = existing with { ResolveSet = resolver };
                return;
            }

            registrations[entityType] = new CrudEntityRegistration(entityType.Name, entityType)
            {
                ResolveSet = resolver
            };
        }
    }

    public DbSet<TEntity> ResolveSet<TEntity>(IServiceProvider serviceProvider) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        CrudEntityRegistration? registration;
        lock (gate)
        {
            registrations.TryGetValue(typeof(TEntity), out registration);
        }

        if (registration?.ResolveSet == null)
        {
            throw new InvalidOperationException($"No resolver registered for entity {typeof(TEntity).Name}");
        }

        var result = registration.ResolveSet(serviceProvider);
        if (result is DbSet<TEntity> dbSet)
        {
            return dbSet;
        }

        throw new InvalidOperationException($"Resolver for entity {typeof(TEntity).Name} did not return a DbSet");
    }
}
