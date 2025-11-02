Feature: Read includes

  Scenario: Reads with allowed include return joined data
    Given a configured CrudQL service with entity "Product" allowing include "category" for role "catalog-admin"
    And an authenticated user with role "catalog-admin"
    And the data store contains a product "Keyboard" linked to category "Peripherals"
    When the client sends a read request for "Product" selecting ["id", "name", { "category": ["id", "title"] }]
    Then the response status is 200
    And the payload contains the product "Keyboard" with category title "Peripherals"

  Scenario: Reads reject includes not permitted for the caller
    Given a configured CrudQL service with entity "Product" allowing include "category" for role "catalog-admin"
    And an authenticated user without role "catalog-admin"
    And the data store contains a product "Keyboard" linked to category "Peripherals"
    When the client sends a read request for "Product" selecting ["id", "name", { "category": ["id", "title"] }]
    Then the response status is 422
    And the payload message states that include "category" is not allowed for the current user
