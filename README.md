# .Net CRUD-QL 

[![NuGet Version](https://img.shields.io/nuget/v/Reqnroll)](https://www.nuget.org/packages/CrudQL.Service)

## 🚀 Overview

**CRUD-QL** is an entity-oriented query builder for **ASP.NET Core** and **EF Core** that provides a unique endpoint for  
`create`, `read`, `update`, and `delete` — without manually writing resolvers.

It includes:

- ✅ Authentication and authorization (RBAC / ABAC)
- ✅ Validation using **FluentValidation**
- ✅ Query builder with filters, ordering, and pagination
- ✅ Secure field projection (only exposes authorized fields)
- ✅ Automatic entity registration (`.AddEntity<T>()`)

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
- `PUT /crud`
- `DELETE /crud`

---

## 🧩 Architecture

1. **Transport** – HTTP endpoint (`/crud`) accepting JSON (JSON-QL)
2. **Validation** – Parses requests and validates entity/fields
3. **AuthN & AuthZ** – Authentication and policy-based authorization
4. **Execution** – Expression Tree builder → EF Core → materialization

---

## 📦 JSON-QL Examples

### Read
```json
{
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
  "entity": "Product",
  "input": { "name": "Mouse Pro", "description": "Wireless", "price": 129.9, "currency": "USD" },
  "returning": ["id", "name", "description", "price", "currency"]
}
```

### Update
```json
{
  "entity": "Product",
  "condition": { "field": "id", "op": "eq", "value": 42 },
  "update": { "description": "Low profile 60%", "price": 430.0 }
}
```

---

## 💡 Include Joins Tutorial

CRUD-QL now supports entity joins on `GET /crud` through the `select` clause. Follow the steps below to enable includes safely.

### 1. Map allowed includes during bootstrapping
Use `AllowInclude` when registering each entity. Provide the navigation path and an optional list of roles that may request it.

```csharp
builder.Services.AddCrudQl()
    .AddEntity<Product>(cfg =>
    {
        cfg.AllowInclude("category", "catalog-admin");
        cfg.AllowInclude("category.products");
    })
    .AddEntity<Category>()
    .AddEntitiesFromDbContext<AppDbContext>();
```

- Paths follow the EF Core navigation chain (`category`, `category.products`, `category.products.supplier`, ...).
- When no roles are supplied the include is available to every caller. Supplying one or more roles restricts usage to principals containing at least one matching `ClaimTypes.Role`.

### 2. Issue a read request with nested `select`
Request the relationship using an object inside the `select` array. This example retrieves a product with its category fields:

```json
{
  "entity": "Product",
  "select": [
    "id",
    "name",
    { "category": ["id", "title"] }
  ]
}
```

Endpoints can still accept the same shape via the `select` query string parameter by providing a JSON fragment.

### 3. Understand authorization feedback
- If a caller requests an include that is not registered via `AllowInclude`, the service returns **422 Unprocessable Entity** with the message `Include '<path>' is not allowed for the current user`.
- Unknown scalar or navigation fields continue to yield **400 Bad Request**.

### 4. Chaining deeper relationships
Once a path segment is allowed, you can expose deeper joins by registering the longer path. For example:

```csharp
cfg.AllowInclude("category");
cfg.AllowInclude("category.products");
cfg.AllowInclude("category.products.category");
```

Clients may then request:

```json
{
  "entity": "Product",
  "select": [
    "id",
    { "category": [
        "title",
        { "products": ["id", "name"] }
      ]
    }
  ]
}
```

This keeps the `/crud` surface single-route while empowering callers to shape responses within the guardrails that you configure.

---


## 🔐 Authentication & Authorization

### RBAC (Role-Based Access Control)
Each user role maps to allowed actions (`Read`, `Create`, `Update`, `Delete`).

### ABAC (Attribute-Based Access Control)
Rules are defined per entity row or field, such as:
> `Product.TenantId == user.TenantId`

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

Validators automatically run in the CRUD pipelines.

Register validators with `cfg.UseValidator(...)` while adding your entity. When no action is provided the validator is attached to the create pipeline; pass one or more `CrudAction` values to target update and delete operations as needed.

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
| **Auth** | RBAC, ABAC (row- and field-level) |
| **Validation** | FluentValidation |
| **Pagination** | Offset or keyset pagination |
| ~~Projections~~ | ~~Field-level access control~~ |
| ~~Observability~~ | ~~Structured logging, tracing, and metrics~~ |
| ~~Extensibility~~ | ~~Interceptors~~ |

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
        cfg.UseValidator(new ProductUpdateValidator(), CrudAction.Update);
        cfg.UseValidator(new ProductDeleteValidator(), CrudAction.Delete);
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

## 📜 License

MIT © 2025 – Forgate Labs  
Built with ❤️ by [Eduardo Cunha]
