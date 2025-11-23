Feature: Product anonymous access

  Scenario: Anonymous user can read products when AllowAnonymous is configured
    Given the Product policy allows anonymous access for Read action
    And the authenticated user has roles Admin
    And the product catalog is empty
    When I create the following products through POST /crud
      | Name     | Description    | Price | Currency |
      | Keyboard | Mechanical 60% | 450   | BRL      |
    And the user is not authenticated
    When I GET /crud for Product selecting id,name,description,price,currency
    Then the response contains the updated products
      | Name     | Description    | Price |
      | Keyboard | Mechanical 60% | 450   |

  Scenario: Anonymous user is blocked from creating when only read is anonymous
    Given the Product policy allows anonymous access for Read action
    And the Product policy allows only Admin for Create action
    And the user is not authenticated
    And the product catalog is empty
    When I attempt to create the following products through POST /crud expecting Unauthorized
      | Name     | Description    | Price | Currency |
      | Keyboard | Mechanical 60% | 450   | BRL      |
    Then the last response status is Unauthorized

  Scenario: Anonymous user is blocked from updating when only read is anonymous
    Given the Product policy allows anonymous access for Read action
    And the Product policy allows only Admin for Update action
    And the authenticated user has roles Admin
    And the product catalog is empty
    When I create the following products through POST /crud
      | Name     | Description    | Price | Currency |
      | Keyboard | Mechanical 60% | 450   | BRL      |
    And the user is not authenticated
    When I attempt to update the following products through PUT /crud expecting Unauthorized
      | Name     | NewDescription  | NewPrice |
      | Keyboard | Low profile 60% | 430      |
    Then the last response status is Unauthorized

  Scenario: Anonymous user is blocked from deleting when only read is anonymous
    Given the Product policy allows anonymous access for Read action
    And the Product policy allows only Admin for Delete action
    And the authenticated user has roles Admin
    And the product catalog is empty
    When I create the following products through POST /crud
      | Name     | Description    | Price | Currency |
      | Keyboard | Mechanical 60% | 450   | BRL      |
    And the user is not authenticated
    When I attempt to delete the following products through DELETE /crud expecting Unauthorized
      | Name     |
      | Keyboard |
    Then the last response status is Unauthorized
