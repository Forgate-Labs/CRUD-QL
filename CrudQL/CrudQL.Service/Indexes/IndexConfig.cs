namespace CrudQL.Service.Indexes;

public sealed record IndexConfig(
    IReadOnlyCollection<IndexDefinition> Indexes
);

public sealed record IndexDefinition(
    string Name,
    IReadOnlyCollection<IndexField> Fields,
    bool IsUnique = false,
    string? Filter = null
);

public sealed record IndexField(
    string FieldName,
    IndexSortOrder SortOrder = IndexSortOrder.Ascending
);

public enum IndexSortOrder
{
    Ascending,
    Descending
}
