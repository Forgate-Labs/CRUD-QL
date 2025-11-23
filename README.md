# CRUD‑QL for .NET

[![NuGet Version](https://img.shields.io/nuget/v/CrudQL.Service)](https://www.nuget.org/packages/CrudQL.Service)
[![NuGet Downloads](https://img.shields.io/nuget/dt/CrudQL.Service)](https://www.nuget.org/packages/CrudQL.Service)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

Build secure, declarative CRUD over EF Core via a single endpoint.

## 🚀 Overview

CRUD‑QL lets you ship CRUD APIs faster by turning EF Core entities into a secure, declarative, single‑route endpoint. Ditch boilerplate controllers and focus on domain rules, not plumbing.

## ✨ Why CRUD‑QL

- Ship faster: no per‑entity controllers/DTO mapping.
- Secure by default: RBAC/ABAC and allowed includes guard data shape.
- Validation first: FluentValidation on create/update and read filters.
- Shape responses: field projection + masked values by role.
- Works with your DbContext: auto‑registration from EF Core.

## 📦 Install

```
dotnet add package CrudQL.Service
```

## 📚 Documentation

- Wiki (guides, setup, payloads): https://github.com/Forgate-Labs/CRUD-QL/wiki
- Start with the end‑to‑end tutorial: https://github.com/Forgate-Labs/CRUD-QL/wiki/Real-World-Case

## 💡 Concept

Expose full CRUD for any entity with a couple of lines:

```csharp
builder.Services.AddCrudQl()
    .AddEntity<Product>()
    .AddEntity<Customer>();

app.MapCrudQl(); // exposes /crud
```

This enables:

- `GET /crud`
- `POST /crud`
- `PUT /crud`
- `DELETE /crud`

## 🧩 Architecture

1. Transport — single HTTP endpoint (`/crud`)
2. Validation — request parsing and entity/field validation
3. AuthN & AuthZ — policy‑based authorization (RBAC/ABAC)
4. Execution — Expression Trees → EF Core → materialization

JSON‑QL is a compact JSON shape to express selection, filters, ordering, and safe includes.

## 🧱 Automatic Entity Registration

Automatically discovers your DbSets, wires resolvers, and applies validators/policies from your `DbContext` — without manual plumbing.

## 🧰 Key Features

- Query: nested filters (and/or), sorting, pagination
- Mutations: create/update/delete with validation
- Auth: RBAC/ABAC (row‑ and field‑level)
- Validation: FluentValidation (create/update and read filters)
- Pagination: offset or keyset
- Projections: field‑level access with masking
- Joins: safe includes via allowed paths

## 🧠 Future Extensions

- Aggregations (count/sum/avg) with filters
- Batch operations & uploads
- Source generator for DTOs/configs
- SDKs (TypeScript/.NET) with typings
- Compiled LINQ cache and observability (logging/metrics)

## 🙋 Who Is It For

- Teams building EF Core APIs who want to cut boilerplate.
- Multi‑tenant or role‑sensitive apps that need strict data shaping.
- Squads standardizing CRUD patterns across many entities.

## 🚫 Not a Fit

- Apps needing a full GraphQL server with complex schema federation.
- Scenarios without EF Core or where ORM is not desired.

## 🤝 Contributing

Issues and PRs are welcome. Share feedback and ideas on the repo’s issue tracker.

## 🔐 Security

If you discover a vulnerability, please open a private report or contact the maintainers.

## 📜 License

MIT © 2025 – Forgate Labs
Built with ❤️ by [Eduardo Cunha]
