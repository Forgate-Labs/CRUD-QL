using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace CrudQL.Service.Entities;

public interface ICrudEntityRegistry
{
    IReadOnlyCollection<CrudEntityRegistration> Entities { get; }

    void RegisterEntity(Type entityType);

    void RegisterEntitySetResolver(Type entityType, Func<IServiceProvider, object> resolver);

    DbSet<TEntity> ResolveSet<TEntity>(IServiceProvider serviceProvider) where TEntity : class;
}
