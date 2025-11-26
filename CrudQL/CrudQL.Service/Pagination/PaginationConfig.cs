namespace CrudQL.Service.Pagination;

public sealed record PaginationConfig(
    int DefaultPageSize = 50,
    int MaxPageSize = 1000
);
