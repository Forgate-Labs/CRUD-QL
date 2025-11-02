using System.Collections.Generic;

namespace CrudQL.Service.Entities;

public sealed record CrudEntityIncludeRegistration(
    string Name,
    IReadOnlyCollection<string>? Roles,
    IReadOnlyDictionary<string, CrudEntityIncludeRegistration> Children);
