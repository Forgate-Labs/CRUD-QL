# Release Notes - v0.3.1

## 🎉 New Features

### Ordering Support
Sort GET query results by one or multiple fields.

**Query examples:**
```
GET /crud?entity=Product&orderBy=price:asc
GET /crud?entity=Product&orderBy=price:asc,name:desc
```

**Configuration:**
```csharp
cfg.ConfigureOrdering(ordering =>
{
    ordering.AllowOrderBy(p => p.Price, p => p.Name);
    ordering.WithDefault(p => p.CreatedAt, OrderDirection.Descending);
});
```

- By default, all fields can be ordered
- Use `AllowOrderBy()` to restrict to specific fields
- Use `WithDefault()` to set default ordering when not specified

---

### Pagination Support
Offset-based pagination with configurable limits.

**Query examples:**
```
GET /crud?entity=Product&page=1&pageSize=20
GET /crud?entity=Product&page=2&pageSize=20&includeCount=true
```

**Response format:**
```json
{
  "data": [...],
  "pagination": {
    "page": 1,
    "pageSize": 20,
    "hasNextPage": true,
    "hasPreviousPage": false,
    "totalRecords": 42,  // only when includeCount=true
    "totalPages": 3      // only when includeCount=true
  }
}
```

**Configuration:**
```csharp
cfg.ConfigurePagination(defaultPageSize: 50, maxPageSize: 100);
```

**Defaults:** `defaultPageSize: 50`, `maxPageSize: 1000`

---

### Database Index Configuration
Declarative index configuration with automatic EF Core integration.

**Examples:**
```csharp
cfg.ConfigureIndexes(indexes =>
{
    // Simple index
    indexes.HasIndex(p => p.Price);

    // Composite index
    indexes.HasIndex(idx =>
    {
        idx.HasField(p => p.Category, IndexSortOrder.Ascending);
        idx.HasField(p => p.Price, IndexSortOrder.Descending);
    }, indexName: "IX_Product_Category_Price");

    // Unique index
    indexes.HasIndex(idx =>
    {
        idx.HasField(p => p.Name);
        idx.IsUnique();
    });

    // Partial index with filter
    indexes.HasIndex(idx =>
    {
        idx.HasField(p => p.CreatedAt, IndexSortOrder.Descending);
        idx.HasFilter("[IsDeleted] = 0");
    }, indexName: "IX_Product_Active");
});
```

**✨ Automatic application:** Indexes are applied automatically to EF Core model via `IModelCustomizer`. No need to modify `OnModelCreating`!

After configuration, just run:
```bash
dotnet ef migrations add AddIndexes
dotnet ef database update
```

---

## 🔄 Combining Features

All features work together seamlessly:

```
GET /crud?entity=Product
    &orderBy=price:asc
    &page=1
    &pageSize=20
    &includeCount=true
    &select=id,name,price
    &filter=%7B%22field%22%3A%22price%22%2C%22op%22%3A%22gte%22%2C%22value%22%3A10%7D
```

This query filters, orders, paginates, and selects specific fields in a single request.

---

## 📚 Documentation

Full documentation available in the wiki:
- [Read - Ordering](https://github.com/your-repo/CRUD-QL/wiki/Read#ordering-orderby)
- [Read - Pagination](https://github.com/your-repo/CRUD-QL/wiki/Read#pagination)
- [Configuration - Database Indexes](https://github.com/your-repo/CRUD-QL/wiki/Configuration-Builder-Services#10-database-index-configuration-v031)

---