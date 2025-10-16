## Implementation plan for issue #14

1. Extend CrudQL service routing
   - Add an in-memory store and per-entity `/products` CRUD endpoints alongside `/crud` for functional coverage.
   - Ensure operations support create, read, update, and delete to satisfy the scenario expectations.
2. Capture the product CRUD lifecycle scenario
   - Introduce `ProductCrudLifecycle.feature` under the Reqnroll suite with the approved Given/When/Then steps.
   - Tag the scenario for regression coverage and align column headers with service contracts.
3. Provide step bindings for HTTP-driven verification
   - Build a test host that registers the entity, seeds requests, and exercises the REST endpoints.
   - Track created product identifiers to drive updates and deletes, asserting responses after each phase.
4. Validate through the test suite
   - Add any missing dependencies (e.g., `Microsoft.AspNetCore.Mvc.Testing`) needed for the host.
   - Execute `dotnet test CrudQL/CrudQL.sln` and ensure the new scenario passes with coverage enabled.
