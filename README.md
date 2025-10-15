# CRUD-QL
> A reimagined GraphQL built for CRUD simplicity — with built-in authentication, authorization, and validation.

## 🚀 Overview

**CRUD-QL** is an entity-oriented query builder for **ASP.NET Core** and **EF Core** that provides a unique endpoint for  
`create`, `read`, `update`, and `delete` — without manually writing resolvers.

It includes:

- ✅ Authentication and authorization (RBAC / ABAC)
- ✅ Validation using **FluentValidation**
- ✅ Query builder with filters, ordering, and pagination
- ✅ Secure field projection (only exposes authorized fields)
- ✅ Automatic entity registration (`.AddEntity<T>()`)
- ✅ Extensible and transport-agnostic (JSON-QL today, GraphQL tomorrow)

---

## 💡 Concept

Developers can expose full CRUD operations for any entity with a single line:

```csharp
builder.Services.AddCrudQl()
    .AddEntity<Product>()
    .AddEntity<Customer>();
```

This automatically enables endpoints such as:

- `GET /crud`
- `POST /crud`
- `PATCH /crud`
- `DELETE /crud`

---

## 🧩 Architecture

CRUD-QL is organized into **five main layers**:

1. **Transport** – HTTP endpoint (`/crud`) accepting JSON (JSON-QL)
2. **Parsing / Schema** – Parses requests and validates entity/fields
3. **AuthN & AuthZ** – Authentication and policy-based authorization
4. **Validation** – FluentValidation per entity and operation
5. **Execution** – Expression Tree builder → EF Core → materialization

---

## 📦 JSON-QL Examples

### Read
```json
{
  "operation": "read",
  "entity": "Product",
  "select": ["id", "name", "price", { "category": ["id", "title"] }],
  "filter": {
    "and": [
      { "field": "price", "op": "gte", "value": 10.0 },
      { "field": "name", "op": "contains", "value": "pro" }
    ]
  },
  "orderBy": [{ "field": "price", "dir": "desc" }],
  "page": { "size": 20 }
}
```

### Create
```json
{
  "operation": "create",
  "entity": "Product",
  "input": { "name": "Mouse Pro", "price": 129.9, "categoryId": 3 },
  "returning": ["id", "name", "price"]
}
```

---

## 🔐 Authentication & Authorization

### RBAC (Role-Based Access Control)
Each user role maps to allowed actions (`Read`, `Create`, `Update`, `Delete`).

### ABAC (Attribute-Based Access Control)
Rules are defined per entity row or field, such as:
> `Product.TenantId == user.TenantId`

```csharp
public interface IAuthzPolicy<T>
{
    Expression<Func<T, bool>> RowPredicate(ClaimsPrincipal user);
    bool CanReadField(ClaimsPrincipal user, string field);
    bool CanDo(ClaimsPrincipal user, CrudAction action);
}
```

---

## 🧾 Validation (FluentValidation)

Each entity can define specific validators per operation:

```csharp
public class ProductCreateValidator : AbstractValidator<Product>
{
    public ProductCreateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
    }
}
```

Validators automatically run in the `Create`, `Update`, and `Delete` pipelines.

---

## ⚙️ Execution Pipeline

### High-Level Steps
1. Authenticate request (JWT / cookie / OAuth)
2. Parse and validate schema
3. Authorize operation and fields
4. Run validation (FluentValidation)
5. Build query with filters, sorting, pagination
6. Execute EF Core
7. Project safe response shape

### Generic Query Example
```csharp
public async Task<object> ExecuteQueryAsync(QueryRequest req, ClaimsPrincipal user)
{
    var cfg = _registry.Get(req.Entity);
    var query = _db.Set(cfg.ClrType).AsQueryable();

    // Row-level policy
    query = Queryable.Where((dynamic)query, (dynamic)cfg.RowLevelPredicate(user));

    // Apply filters, ordering, and projection
    query = FilterBuilder.Apply(query, req.Filter);
    query = SortBuilder.Apply(query, req.OrderBy);
    var projected = ProjectionBuilder.Project(query, cfg.ClrType, req.Select);

    return await projected.ToListAsync();
}
```

---

## 🧮 Query Builder (Expression Trees)

Filters are dynamically converted to LINQ expressions.

Example filter:
```json
{
  "and": [
    { "field": "price", "op": "gte", "value": 10 },
    { "field": "name", "op": "contains", "value": "Pro" }
  ]
}
```

Becomes:
```csharp
x => x.Price >= 10 && x.Name.Contains("Pro")
```

---

## 🧱 Automatic Entity Registration

```csharp
builder.Services.AddCrudQl()
    .AddEntitiesFromDbContext<AppDbContext>();
```

Every `DbSet<T>` implementing `IEntity<TKey>` is automatically discovered and registered with validators and policies.

---

## 🧰 Key Features

| Category | Features |
|-----------|-----------|
| **Query** | Nested filters (`and` / `or`), sorting, cursor pagination |
| **Mutations** | Create / Update / Delete with validation |
| **Auth** | JWT, RBAC, ABAC (row- and field-level) |
| **Validation** | FluentValidation + operation-specific rules |
| **Pagination** | Offset or keyset pagination |
| **Projections** | Field-level access control |
| **Observability** | Structured logging, tracing, and metrics |
| **Extensibility** | Custom validators, policies, interceptors |

---

## 🧠 Future Extensions

- [ ] Aggregations (`count`, `sum`, `avg`) with filters  
- [ ] Batch operations & file uploads  
- [ ] Source Generator to create DTOs and configs automatically  
- [ ] SDKs (TypeScript / .NET) with generated typings  
- [ ] Subscriptions (SignalR / WebSocket)  
- [ ] Automatic keyset pagination  

---

## 📊 Full Example

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication("Bearer").AddJwtBearer();
builder.Services.AddAuthorization();
builder.Services.AddDbContext<AppDbContext>();

builder.Services.AddCrudQl()
    .AddEntity<Product>(cfg =>
    {
        cfg.UseValidator(new ProductCreateValidator());
        cfg.UsePolicy(new ProductPolicy());
    })
    .AddEntitiesFromDbContext<AppDbContext>();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapCrudQl(); // Exposes /crud endpoint
app.Run();
```

---

## 🧪 Development Roadmap

### MVP
- JSON-QL parser + CRUD operations  
- AuthN/AuthZ (RBAC/ABAC)  
- FluentValidation integration  
- Query builder + projections  
- Logging and metrics  

### Phase 2
- Field-level authorization  
- Aggregations and computed fields  
- SDK with strong typing  
- Compiled LINQ expression cache  

---

## 🏗 Project Structure (suggested)

```
CrudQl/
 ├── CrudQl.Core/
 │   ├── Execution/
 │   ├── Validation/
 │   ├── Auth/
 │   ├── Expressions/
 │   └── Extensions/
 ├── CrudQl.Web/
 │   ├── Controllers/
 │   └── Middleware/
 └── CrudQl.Tests/
     ├── QueryTests.cs
     └── AuthTests.cs
```

---

## 📜 License

MIT © 2025 – Forgate Labs  
Built with ❤️ by [Eduardo Cunha]
