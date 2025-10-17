using System;
using Microsoft.EntityFrameworkCore;

namespace CrudQL.Service.Entities;

public sealed record CrudEntityRegistration(string EntityName, Type ClrType)
{
    public Func<IServiceProvider, object>? ResolveSet { get; init; }

    public Func<IServiceProvider, DbContext>? ResolveContext { get; init; }
}
