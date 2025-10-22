using System;
using CrudQL.Service.Authorization;
using CrudQL.Service.Entities;

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
}
