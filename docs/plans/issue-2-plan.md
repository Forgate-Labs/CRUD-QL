## Implementation plan for issue #2

1. Define the CrudQL builder contract
   - Add `CrudQL.Service/Configuration/ICrudQlBuilder.cs` exposing the `IServiceCollection Services` property.
   - Include fluent members `AddEntity<TEntity>()` and `AddEntitiesFromDbContext<TContext>()`, both returning `ICrudQlBuilder`.
2. Implement the builder
   - Create `CrudQL.Service/Configuration/CrudQlBuilder.cs` that captures the incoming `IServiceCollection`.
   - Ensure fluent members return `this` so consumers can chain future configuration calls.
3. Provide the service collection extension entry point
   - Add `CrudQL.Service/DependencyInjection/CrudQlServiceCollectionExtensions.cs` with `AddCrudQl(this IServiceCollection services)`.
   - Guard against null `services`, instantiate `CrudQlBuilder`, and return it.
4. Remove obsolete scaffolding
   - Delete `CrudQL.Service/Class1.cs` now that the builder and extension provide the public surface.
5. Validate through BDD suite
   - Run `dotnet test CrudQL/CrudQL.sln` to execute the new feature and ensure the builder contract is satisfied.
