Feature: CrudQL Swagger documentation

  Scenario Outline: Swagger documents the <verb> payload contract
    Given a web application instance
    When I map CrudQL with MapCrudQl
    Then the endpoint set should contain /crud for <verb>
    And the Swagger metadata for <verb> should describe the payload fields "<fields>"

    Examples:
      | verb   | fields                         |
      | POST   | entity,input,returning         |
      | GET    | entity,filter,select,returning |
      | PUT    | entity,condition,update        |
      | DELETE | entity,key                     |

  Scenario Outline: Swagger documents the <verb> response contract
    Given a web application instance
    When I map CrudQL with MapCrudQl
    Then the endpoint set should contain /crud for <verb>
    And the Swagger metadata for <verb> should describe the "<status>" response with "<properties>"

    Examples:
      | verb   | status | properties     |
      | POST   | 201    | data           |
      | GET    | 200    | data           |
      | PUT    | 200    | affectedRows   |
      | DELETE | 204    | (empty body)   |
