# Repository Guidelines

## Project Structure & Module Organization
- `CrudQL/CrudQL.sln` is the entry point that ties together the service library and test suite.
- `CrudQL/CrudQL.Service/` hosts the core .NET 8 library; keep new source files under coherent folders (e.g., `Authorization/`, `Validation/`) as features mature.
- `CrudQL/CrudQL.Tests/` hosts Reqnroll feature files and step bindings; mirror the service namespace structure so that each capability has matching specs.
- `bin/` and `obj/` directories are build artifacts—never commit them.
- The platform exposes a single HTTP entry point: `/crud`. No additional routes may be introduced; every entity operation must flow through `/crud` using the generic CrudQL JSON payloads.

## Project Task Management
- Treat GitHub Project 2 as the authoritative backlog; review its items to decide what to work on next.
- Represent every planned task as a real repository issue in `eduardofacunha/CRUD-QL` and link it to Project 2, avoiding draft-only project items.
- When planning wraps up for a new task, create the corresponding issue immediately and place it in Project 2 with the appropriate status.
- Ensure every newly created issue is added to GitHub Project 2 and assigned the Backlog status.

## Collaboration Workflow
- Treat the beginning of every chat session as a new task entering the pipeline; follow the backlog intake and planning process unless the user explicitly calls out a hotfix during that same session.
- Backlog intake: the user requests a new task, collaborates on gathering requirements, and we create the issue in Project 2 with status Backlog once the information set is complete.
- Planning stage: create branch `task/{issue-id}` from `main`, outline the implementation plan, author the required BDD scenarios and bindings (fully defined and implemented), communicate the full plan and BDD scenarios in chat, and submit the plan for user approval; upon approval move the issue to Planned, record the test files that need implementation in the issue description, append the approved plan text to the issue description, and commit/push the planning artifacts to origin; subsequent stages may not modify the approved scenarios or bindings.
- Readiness gate: after technical and business validation, move the issue card to Ready.
- Delivery stage: pull the first Ready issue, move it to In Progress, implement the work, run tests, commit with the issue ID in the message, push to origin, and shift the issue to In review.
- Review stage: once an issue moves to In review, open a pull request from the task branch to main so reviewers can begin validation.

### Planning Response Template
```
Plan for issue #<id>

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
- Follow the existing imperative style (`Enhance README…`, `Add validator`) and keep subject lines under 72 characters.
- Bundle related changes in a single commit; avoid mixing refactors and feature work without clear justification.
- Pull request descriptions must include the following sections in order: `## Summary`, `## Testing`, and `## Deployment/Rollback`.
- Pull requests should summarize scope, link related issues, and list validation steps (build, tests, coverage runs).
- Include screenshots or sample payloads when modifying transport-layer behavior so reviewers can validate the contract quickly.
- Issues, commits, and pull requests can be created using the `gh` command in Bash.
- Always end the pull request description with the line `Close #{issue-number}` to ensure the issue is closed automatically on merge.
