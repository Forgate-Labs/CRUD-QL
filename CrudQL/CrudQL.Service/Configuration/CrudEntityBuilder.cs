using System;
using System.Collections.Generic;
using CrudQL.Service.Authorization;
using CrudQL.Service.Entities;
using CrudQL.Service.Validation;
using FluentValidation;

namespace CrudQL.Service.Configuration;

public sealed class CrudEntityBuilder<TEntity>
{
    private readonly ICrudEntityRegistry registry;

    public CrudEntityBuilder(ICrudEntityRegistry registry)
    {
        this.registry = registry;
    }

    public CrudEntityBuilder<TEntity> UsePolicy(ICrudPolicy<TEntity> policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        registry.SetPolicy(typeof(TEntity), policy);
        return this;
    }

    public CrudEntityBuilder<TEntity> UseValidator(IValidator<TEntity> validator, params CrudAction[] actions)
    {
        ArgumentNullException.ThrowIfNull(validator);
        var targetActions = actions is { Length: > 0 } ? actions : new[] { CrudAction.Create };
        registry.AddValidator(typeof(TEntity), typeof(TEntity), validator, targetActions);
        return this;
    }

    public CrudEntityBuilder<TEntity> UseFilterValidator(IValidator<CrudFilterContext> validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        registry.AddValidator(typeof(TEntity), typeof(CrudFilterContext), validator, new[] { CrudAction.Read });
        return this;
    }
}
