using System.Collections.Generic;

namespace CrudQL.Service.Routing;

internal sealed record CrudQlOperationDocumentation(
    string Method,
    IReadOnlyCollection<string> RequestFields,
    IReadOnlyCollection<CrudQlOperationResponseDocumentation> Responses);

internal sealed record CrudQlOperationResponseDocumentation(
    int StatusCode,
    IReadOnlyCollection<string>? Properties);
