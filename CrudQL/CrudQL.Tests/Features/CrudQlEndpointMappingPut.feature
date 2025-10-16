Feature: CrudQL endpoint mapping PUT
  Scenario: MapCrudQl exposes PUT /crud
    Given a web application instance
    When I map CrudQL with MapCrudQl
    Then the endpoint set should contain /crud for PUT
    And calling the /crud endpoint for PUT should return ok
