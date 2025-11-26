using System;
using System.Collections.Generic;
using CrudQL.Service.Authorization;
using CrudQL.Service.Entities;
using CrudQL.Service.Indexes;
using CrudQL.Service.Ordering;
using CrudQL.Service.Pagination;
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

    public CrudEntityBuilder<TEntity> AllowInclude(string includePath, params string[] roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(includePath);
        var allowedRoles = roles is { Length: > 0 } ? roles : null;
        registry.AddInclude(typeof(TEntity), includePath, allowedRoles);
        return this;
    }

    public CrudEntityBuilder<TEntity> ConfigurePagination(int defaultPageSize = 50, int maxPageSize = 1000)
    {
        if (defaultPageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultPageSize), "Default page size must be at least 1");
        }

        if (maxPageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPageSize), "Max page size must be at least 1");
        }

        if (defaultPageSize > maxPageSize)
        {
            throw new ArgumentException("Default page size cannot exceed max page size");
        }

        registry.SetPaginationConfig(typeof(TEntity), new PaginationConfig(defaultPageSize, maxPageSize));
        return this;
    }

    public CrudEntityBuilder<TEntity> ConfigureIndexes(Action<IndexConfigBuilder<TEntity>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new IndexConfigBuilder<TEntity>();
        configure(builder);
        var indexConfig = builder.Build();

        registry.SetIndexConfig(typeof(TEntity), indexConfig);
        return this;
    }

    public CrudEntityBuilder<TEntity> ConfigureOrdering(Action<OrderByConfigBuilder<TEntity>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new OrderByConfigBuilder<TEntity>();
        configure(builder);
        var orderByConfig = builder.Build();

        registry.SetOrderByConfig(typeof(TEntity), orderByConfig);
        return this;
    }
}
