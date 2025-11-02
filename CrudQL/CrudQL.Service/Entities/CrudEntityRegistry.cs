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

    public void AddInclude(Type entityType, string includePath, IReadOnlyCollection<string>? roles)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(includePath);

        var segments = includePath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            throw new ArgumentException("Include path must contain at least one segment.", nameof(includePath));
        }

        lock (gate)
        {
            if (registrations.TryGetValue(entityType, out var existing))
            {
                var updated = existing with { Includes = MergeIncludes(existing.Includes, segments, roles) };
                registrations[entityType] = updated;
                registrationsByName[updated.EntityName] = updated;
                return;
            }

            var registration = new CrudEntityRegistration(entityType.Name, entityType)
            {
                Includes = MergeIncludes(new Dictionary<string, CrudEntityIncludeRegistration>(StringComparer.OrdinalIgnoreCase), segments, roles)
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

    private static IReadOnlyDictionary<string, CrudEntityIncludeRegistration> MergeIncludes(
        IReadOnlyDictionary<string, CrudEntityIncludeRegistration> existing,
        IReadOnlyList<string> segments,
        IReadOnlyCollection<string>? roles)
    {
        var target = CopyIncludes(existing);
        AddIncludeSegment(target, segments, 0, roles);
        return CopyIncludes(target);
    }

    private static void AddIncludeSegment(
        IDictionary<string, CrudEntityIncludeRegistration> target,
        IReadOnlyList<string> segments,
        int index,
        IReadOnlyCollection<string>? roles)
    {
        var segment = segments[index];
        target.TryGetValue(segment, out var existing);
        var normalizedRoles = NormalizeRoles(roles);
        var children = existing != null
            ? CopyIncludes(existing.Children)
            : new Dictionary<string, CrudEntityIncludeRegistration>(StringComparer.OrdinalIgnoreCase);

        var mergedRoles = existing == null ? normalizedRoles : MergeRoles(existing.Roles, normalizedRoles);
        if (index < segments.Count - 1)
        {
            AddIncludeSegment(children, segments, index + 1, normalizedRoles);
        }

        target[segment] = new CrudEntityIncludeRegistration(segment, mergedRoles, CopyIncludes(children));
    }

    private static IReadOnlyCollection<string>? MergeRoles(IReadOnlyCollection<string>? existing, IReadOnlyCollection<string>? incoming)
    {
        if (incoming == null)
        {
            return null;
        }

        if (existing == null)
        {
            return incoming;
        }

        var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        foreach (var role in incoming)
        {
            set.Add(role);
        }

        return set.ToArray();
    }

    private static IReadOnlyCollection<string>? NormalizeRoles(IReadOnlyCollection<string>? roles)
    {
        if (roles == null)
        {
            return null;
        }

        var set = new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
        if (set.Count == 0)
        {
            return Array.Empty<string>();
        }

        return set.ToArray();
    }

    private static Dictionary<string, CrudEntityIncludeRegistration> CopyIncludes(IReadOnlyDictionary<string, CrudEntityIncludeRegistration> source)
    {
        var result = new Dictionary<string, CrudEntityIncludeRegistration>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            result[pair.Key] = pair.Value;
        }

        return result;
    }
}
