Feature: CrudQL service registration
  In order to bootstrap CrudQL in consuming applications
  As a service developer
  I want an AddCrudQl entry point that returns a configurable builder

  Scenario: AddCrudQl exposes the CrudQL builder contract
    Given an empty service collection
    When I configure CrudQL via AddCrudQl
    Then the AddCrudQl call should return the CrudQL builder
    And the builder should expose the registered services for chaining
    And the builder contract should expose entity registration methods
