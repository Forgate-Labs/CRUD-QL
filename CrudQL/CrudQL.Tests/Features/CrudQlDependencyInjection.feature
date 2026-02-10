Feature: CrudQL dependency injection validation
  In order to avoid runtime failures in consuming applications
  As a service developer
  I want CrudQL services to pass DI scope validation

  Scenario: CrudQL services pass scope validation when configured with a DbContext
    Given an empty service collection with scope validation enabled
    When I register CrudQL with a DbContext and an entity
    Then building the service provider should not throw
    And the IModelCustomizer should be resolvable from a scope
