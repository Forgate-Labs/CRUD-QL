Feature: CrudQL endpoint mapping POST
  Scenario: MapCrudQl exposes POST /crud
    Given a web application instance
    When I map CrudQL with MapCrudQl
    Then the endpoint set should contain /crud for POST
    And calling the /crud endpoint for POST should return ok
