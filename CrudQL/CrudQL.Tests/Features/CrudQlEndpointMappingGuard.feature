Feature: CrudQL endpoint mapping guard
  Scenario: MapCrudQl rejects null builders
    Given a null endpoint route builder
    When I invoke MapCrudQl
    Then an argument null exception should be thrown
