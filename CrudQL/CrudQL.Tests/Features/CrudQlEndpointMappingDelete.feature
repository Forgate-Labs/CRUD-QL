Feature: CrudQL endpoint mapping DELETE
  Scenario: MapCrudQl exposes DELETE /crud
    Given a web application instance
    When I map CrudQL with MapCrudQl
    Then the endpoint set should contain /crud for DELETE
    And calling the /crud endpoint for DELETE should return ok
