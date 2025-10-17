# Repository Guidelines

## Command Execution
- Always execute all repository commands using Bash.

## Project Structure & Module Organization
- `CrudQL/CrudQL.sln` is the entry point that ties together the service library and test suite.
- `CrudQL/CrudQL.Service/` hosts the core .NET 8 library; keep new source files under coherent folders (e.g., `Authorization/`, `Validation/`) as features mature.
- `CrudQL/CrudQL.Tests/` hosts Reqnroll feature files and step bindings; mirror the service namespace structure so that each capability has matching specs.
- `bin/` and `obj/` directories are build artifacts—never commit them.
- The platform exposes a single HTTP entry point: `/crud`. No additional routes may be introduced; every entity operation must flow through `/crud` using the generic CrudQL JSON payloads.

## Collaboration Workflow
- Treat the beginning of every chat session as a new task entering the pipeline; follow the backlog intake and planning process unless the user explicitly calls out a hotfix during that same session.
- Backlog intake: the user requests a new task, collaborates on gathering requirements, and we capture the agreed scope once the information set is complete.
- Planning stage: create branch `task/<task-name>` from `main`, outline the implementation plan, author the required BDD scenarios and bindings (fully defined and implemented), communicate the full plan and BDD scenarios in chat, and submit the plan for user approval; upon approval record the test files that need implementation alongside the approved plan, and commit/push the planning artifacts to origin; subsequent stages may not modify the approved scenarios or bindings.
- Readiness gate: after technical and business validation, confirm the task is ready to implement.
- Delivery stage: pick the first ready task, implement the work, run tests, commit with a clear message, and push to origin.
- Review stage: once the task is ready for review, open a pull request from the task branch to main so reviewers can begin validation.

### Planning Response Template
```
Plan for task <name>

Implementation Plan
1. ...
2. ...

BDD Scenarios
- CrudQL/CrudQL.Tests/Features/<FeatureName>.feature
  Scenario: <title>
    Given ...
    When ...
    Then ...

Bindings
- CrudQL/CrudQL.Tests/Bindings/<BindingName>.cs
```

Always include the complete Given/When/Then steps for every scenario listed.

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
- Bundle related changes in a single commit; avoid mixing refactors and feature work without clear justification.
- Whenever preparing a pull request, include a bulleted section aimed at end users that highlights the key library changes.
- Commits and pull requests can be created using the `gh` command in Bash, and always pass `--fill` when creating pull requests.
