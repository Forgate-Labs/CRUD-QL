using System;
using System.Collections.Generic;
using CrudQL.Service.Authorization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CrudQL.Service.Entities;

public sealed record CrudEntityRegistration(string EntityName, Type ClrType)
{
    public Func<IServiceProvider, object>? ResolveSet { get; init; }

    public Func<IServiceProvider, DbContext>? ResolveContext { get; init; }

    public ICrudPolicy? Policy { get; init; }

    public IReadOnlyDictionary<CrudAction, IReadOnlyList<CrudValidatorRegistration>> Validators { get; init; } =
        new Dictionary<CrudAction, IReadOnlyList<CrudValidatorRegistration>>();
}
