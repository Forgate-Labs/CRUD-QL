# Repository Guidelines

## Project Structure & Module Organization
- `CrudQL/CrudQL.sln` is the entry point that ties together the service library and test suite.
- `CrudQL/CrudQL.Service/` hosts the core .NET 8 library; keep new source files under coherent folders (e.g., `Authorization/`, `Validation/`) as features mature.
- `CrudQL/CrudQL.Tests/` hosts Reqnroll feature files and step bindings; mirror the service namespace structure so that each capability has matching specs.
- `bin/` and `obj/` directories are build artifacts—never commit them.

## Build, Test, and Development Commands
- `dotnet restore CrudQL/CrudQL.sln` — restore all solution dependencies; run after pulling new packages.
- `dotnet build CrudQL/CrudQL.sln` — compile the service and tests; add `-c Release` before publishing artifacts.
- `dotnet test CrudQL/CrudQL.sln --collect:"XPlat Code Coverage"` — execute all Reqnroll scenarios while capturing coverage via the bundled Coverlet collector.
- `dotnet watch test --project CrudQL/CrudQL.Tests/CrudQL.Tests.csproj` — iterate quickly while evolving scenarios and bindings.

## Coding Style & Naming Conventions
- Target `net8.0` with nullable reference types and implicit usings enabled; do not disable these project defaults.
- Use four-space indentation, file-scoped namespaces, `PascalCase` for types/methods, and `camelCase` for locals and parameters.
- Keep public APIs self-documenting; prefer early argument validation with informative exceptions.
- Run `dotnet format CrudQL/CrudQL.sln` before publishing a branch to normalize whitespace, usings, and analyzers.
- Do not include comments in source code files.
- Write all source code, BDD feature files, and Markdown documents in English.

## Testing Guidelines
- Create feature files under `CrudQL/CrudQL.Tests/Features/` and step bindings in `CrudQL/CrudQL.Tests/Bindings/` (add directories as needed).
- Drive development with Reqnroll’s red-green BDD loop: write a failing scenario, implement the necessary step definitions, then refactor once the suite passes.
- Keep scenarios human-readable, focused on business intent, and ensure bindings execute only through public service entry points.
- Use tags to organize smoke, regression, and authorization coverage; document any custom tags within the feature file.

## Commit & Pull Request Guidelines
- Follow the existing imperative style (`Enhance README…`, `Add validator`) and keep subject lines under 72 characters.
- Bundle related changes in a single commit; avoid mixing refactors and feature work without clear justification.
- Pull requests should summarize scope, link related issues, and list validation steps (build, tests, coverage runs).
- Include screenshots or sample payloads when modifying transport-layer behavior so reviewers can validate the contract quickly.
- Issues, commits, and pull requests can be created using the `gh` command in Bash.
