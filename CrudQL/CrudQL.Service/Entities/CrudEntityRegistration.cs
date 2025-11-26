using System;
using System.Collections.Generic;
using CrudQL.Service.Authorization;
using CrudQL.Service.Indexes;
using CrudQL.Service.Ordering;
using CrudQL.Service.Pagination;
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

    public IReadOnlyDictionary<string, CrudEntityIncludeRegistration> Includes { get; init; } =
        new Dictionary<string, CrudEntityIncludeRegistration>(StringComparer.OrdinalIgnoreCase);

    public PaginationConfig? PaginationConfig { get; init; }

    public IndexConfig? IndexConfig { get; init; }

    public OrderByConfig? OrderByConfig { get; init; }
}
