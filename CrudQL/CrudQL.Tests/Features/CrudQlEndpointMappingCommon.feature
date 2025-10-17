Feature: CrudQL endpoint mapping

  Scenario Outline: MapCrudQl exposes /crud for <verb>
    Given a web application instance
    When I map CrudQL with MapCrudQl
    Then the endpoint set should contain /crud for <verb>
    And calling the /crud endpoint for <verb> <payload> should return ok

    Examples:
      | verb   | payload                      |
      | GET    |                               |
      | POST   | with the documented payload |
      | PUT    |                               |
      | DELETE |                               |

