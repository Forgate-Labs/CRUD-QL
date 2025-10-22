using System;
using CrudQL.Service.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CrudQL.Service.Entities;

public sealed record CrudEntityRegistration(string EntityName, Type ClrType)
{
    public Func<IServiceProvider, object>? ResolveSet { get; init; }

    public Func<IServiceProvider, DbContext>? ResolveContext { get; init; }

    public ICrudPolicy? Policy { get; init; }
}
