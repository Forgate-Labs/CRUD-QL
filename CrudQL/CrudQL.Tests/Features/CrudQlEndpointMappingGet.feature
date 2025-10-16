Feature: CrudQL endpoint mapping GET
  Scenario: MapCrudQl exposes GET /crud
    Given a web application instance
    When I map CrudQL with MapCrudQl
    Then the endpoint set should contain /crud for GET
    And calling the /crud endpoint for GET with the documented payload should return ok
