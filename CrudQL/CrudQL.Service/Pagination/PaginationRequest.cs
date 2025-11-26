namespace CrudQL.Service.Pagination;

public sealed record PaginationRequest(
    int Page,
    int PageSize,
    bool IncludeCount
);
