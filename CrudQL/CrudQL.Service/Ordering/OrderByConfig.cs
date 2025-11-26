namespace CrudQL.Service.Ordering;

public sealed record OrderByConfig(
    IReadOnlyCollection<string>? AllowedFields = null,
    string? DefaultField = null,
    OrderDirection DefaultDirection = OrderDirection.Ascending
);

public sealed record OrderByRequest(
    IReadOnlyCollection<OrderByField> Fields
);

public sealed record OrderByField(
    string FieldName,
    OrderDirection Direction
);

public enum OrderDirection
{
    Ascending,
    Descending
}
