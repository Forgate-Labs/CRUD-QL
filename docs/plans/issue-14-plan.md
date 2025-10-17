## Implementation plan for issue #14

1. Implement CRUD handling behind /crud
   - Extend the existing CrudQL pipeline so /crud accepts create, read, update, and delete payloads for the Product entity without introducing new routes.
   - Maintain an in-memory store during tests to mimic persistence for successive operations.
2. Capture the product CRUD lifecycle scenario
   - Update `ProductCrudLifecycle.feature` to express the workflow using /crud and generic CrudQL payloads.
   - Keep the regression and products tags for coverage tracking.
3. Provide bindings for CRUD payload execution
   - Adjust the step bindings to post the correct JSON envelopes to /crud and parse the responses for verification.
   - Track created identifiers so the same scenario can update and delete the right records.
4. Validate through the test suite
   - Ensure required dependencies are present.
   - Run `dotnet test CrudQL/CrudQL.sln` once implementation is complete.
