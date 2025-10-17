Feature: CrudQL entity registration
  Scenario: AddEntity registers the entity metadata
    Given an empty service collection
    When I configure CrudQL via AddCrudQl
    And I add the Product entity to CrudQL
    Then the entity registry should list Product with its CLR type

  Scenario: AddEntitiesFromDbContext wires DbContext-backed sets
    Given an empty service collection
    And a DbContext registered with in-memory storage
    When I configure CrudQL via AddCrudQl
    And I add the Product entity to CrudQL
    And I register the entities from the FakeDbContext
    Then resolving the Product entity set should yield an EF Core DbSet
