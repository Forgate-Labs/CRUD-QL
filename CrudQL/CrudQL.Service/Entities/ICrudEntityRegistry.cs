using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace CrudQL.Service.Entities;

public interface ICrudEntityRegistry
{
    IReadOnlyCollection<CrudEntityRegistration> Entities { get; }

    void RegisterEntity(Type entityType);

    void RegisterEntitySetResolver(Type entityType, Func<IServiceProvider, object> setResolver, Func<IServiceProvider, DbContext> contextResolver);

    DbSet<TEntity> ResolveSet<TEntity>(IServiceProvider serviceProvider) where TEntity : class;

    bool TryGetEntity(string entityName, out CrudEntityRegistration registration);
}
