using System;
using System.Collections.Generic;
using CrudQL.Service.Authorization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CrudQL.Service.Entities;

public interface ICrudEntityRegistry
{
    IReadOnlyCollection<CrudEntityRegistration> Entities { get; }

    void RegisterEntity(Type entityType);

    void RegisterEntitySetResolver(Type entityType, Func<IServiceProvider, object> setResolver, Func<IServiceProvider, DbContext> contextResolver);

    void SetPolicy(Type entityType, ICrudPolicy? policy);

    void AddValidator(Type entityType, Type targetType, IValidator validator, IReadOnlyCollection<CrudAction> actions);

    DbSet<TEntity> ResolveSet<TEntity>(IServiceProvider serviceProvider) where TEntity : class;

    bool TryGetEntity(string entityName, out CrudEntityRegistration registration);
}
