namespace CrudQL.Service.Pagination;

public sealed record PaginationResponse(
    int Page,
    int PageSize,
    bool HasNextPage,
    bool HasPreviousPage,
    int? TotalRecords = null,
    int? TotalPages = null
);
