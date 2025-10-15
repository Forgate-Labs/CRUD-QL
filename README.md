# CRUD-QL
> A reimagined GraphQL built for CRUD simplicity — with built-in authentication, authorization, and validation.

## 🚀 Overview

**CRUD-QL** is an entity-oriented query builder for **ASP.NET Core** and **EF Core** that provides universal endpoints for  
`query`, `create`, `update`, and `delete` — without manually writing resolvers.

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
