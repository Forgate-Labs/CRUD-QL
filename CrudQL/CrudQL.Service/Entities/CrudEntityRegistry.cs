using System;
using System.Collections.Generic;
using System.Linq;
using CrudQL.Service.Authorization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CrudQL.Service.Entities;

internal sealed class CrudEntityRegistry : ICrudEntityRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<Type, CrudEntityRegistration> registrations = new();
    private readonly Dictionary<string, CrudEntityRegistration> registrationsByName = new(StringComparer.OrdinalIgnoreCase);

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
            if (registrations.ContainsKey(entityType))
            {
                return;
            }

            var registration = new CrudEntityRegistration(entityType.Name, entityType);
            registrations[entityType] = registration;
            registrationsByName[registration.EntityName] = registration;
        }
    }

    public void RegisterEntitySetResolver(Type entityType, Func<IServiceProvider, object> setResolver, Func<IServiceProvider, DbContext> contextResolver)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(setResolver);
        ArgumentNullException.ThrowIfNull(contextResolver);

        lock (gate)
        {
            if (registrations.TryGetValue(entityType, out var existing))
            {
                var updated = existing with { ResolveSet = setResolver, ResolveContext = contextResolver };
                registrations[entityType] = updated;
                registrationsByName[updated.EntityName] = updated;
                return;
            }

            var registration = new CrudEntityRegistration(entityType.Name, entityType)
            {
                ResolveSet = setResolver,
                ResolveContext = contextResolver
            };
            registrations[entityType] = registration;
            registrationsByName[registration.EntityName] = registration;
        }
    }

    public void SetPolicy(Type entityType, ICrudPolicy? policy)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        lock (gate)
        {
            if (registrations.TryGetValue(entityType, out var existing))
            {
                var updated = existing with { Policy = policy };
                registrations[entityType] = updated;
                registrationsByName[updated.EntityName] = updated;
                return;
            }

            var registration = new CrudEntityRegistration(entityType.Name, entityType)
            {
                Policy = policy
            };
            registrations[entityType] = registration;
            registrationsByName[registration.EntityName] = registration;
        }
    }

    public void AddValidator(Type entityType, Type targetType, IValidator validator, IReadOnlyCollection<CrudAction> actions)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(validator);
        if (actions == null || actions.Count == 0)
        {
            throw new ArgumentException("At least one action must be provided.", nameof(actions));
        }

        lock (gate)
        {
            if (registrations.TryGetValue(entityType, out var existing))
            {
                var updated = existing with { Validators = MergeValidators(existing.Validators, targetType, validator, actions) };
                registrations[entityType] = updated;
                registrationsByName[updated.EntityName] = updated;
                return;
            }

            var registration = new CrudEntityRegistration(entityType.Name, entityType)
            {
                Validators = MergeValidators(new Dictionary<CrudAction, IReadOnlyList<CrudValidatorRegistration>>(), targetType, validator, actions)
            };
            registrations[entityType] = registration;
            registrationsByName[registration.EntityName] = registration;
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

    public bool TryGetEntity(string entityName, out CrudEntityRegistration registration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);

        lock (gate)
        {
            return registrationsByName.TryGetValue(entityName, out registration!);
        }
    }

    private static IReadOnlyDictionary<CrudAction, IReadOnlyList<CrudValidatorRegistration>> MergeValidators(
        IReadOnlyDictionary<CrudAction, IReadOnlyList<CrudValidatorRegistration>> existing,
        Type targetType,
        IValidator validator,
        IReadOnlyCollection<CrudAction> actions)
    {
        var result = existing.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<CrudValidatorRegistration>)pair.Value.ToList(),
            EqualityComparer<CrudAction>.Default);

        foreach (var action in actions)
        {
            if (!result.TryGetValue(action, out var validators))
            {
                result[action] = new List<CrudValidatorRegistration> { new CrudValidatorRegistration(targetType, validator) };
                continue;
            }

            var list = validators.ToList();
            list.Add(new CrudValidatorRegistration(targetType, validator));
            result[action] = list;
        }

        return result.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<CrudValidatorRegistration>)pair.Value.ToList(),
            EqualityComparer<CrudAction>.Default);
    }
}
