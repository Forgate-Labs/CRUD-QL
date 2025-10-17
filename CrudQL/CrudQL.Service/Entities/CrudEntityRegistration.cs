using System;

namespace CrudQL.Service.Entities;

public sealed record CrudEntityRegistration(string EntityName, Type ClrType)
{
    public Func<IServiceProvider, object>? ResolveSet { get; init; }
}
