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

## 📚 Documentation

Looking for end-to-end usage, configuration, and examples? See the wiki:

- https://github.com/Forgate-Labs/CRUD-QL/wiki

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

## 🧱 Automatic Entity Registration

CRUD-QL supports automatic entity registration and policy/validator wiring from your `DbContext`.

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
