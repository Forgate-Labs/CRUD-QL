Feature: Product soft delete

  @regression @products
  Scenario: Soft delete marks Deleted flag and hides products from reads
    Given the Product policy uses soft delete flag Deleted for role Admin
    And the authenticated user has roles Admin
    And the product catalog is empty
    When I create the following products through POST /crud
      | Name           | Description    | Price | Currency |
      | Aurora Lamp    | Desk lighting  | 49.99 | USD      |
      | Borealis Chair | Ergonomic seat | 129.0 | USD      |
    And I delete the following products through DELETE /crud
      | Name        |
      | Aurora Lamp |
    And I GET /crud for Product
    Then the response does not include the deleted product names
    And the EF Core store marks the following products as deleted
      | Name        |
      | Aurora Lamp |

  @regression @products
  Scenario: Deleting an already deleted product returns a bad request
    Given the Product policy uses soft delete flag Deleted for role Admin
    And the authenticated user has roles Admin
    And the product catalog is empty
    When I create the following products through POST /crud
      | Name        | Description    | Price | Currency |
      | Aurora Lamp | Desk lighting  | 49.99 | USD      |
    And I delete the following products through DELETE /crud
      | Name        |
      | Aurora Lamp |
    When I attempt to delete the following products through DELETE /crud expecting 400
      | Name        |
      | Aurora Lamp |
    Then the last response message is "Entity is already deleted"

  @regression @products
  Scenario: Soft delete records deletion time using UTC
    Given the Product policy uses soft delete flag Deleted and timestamp DeletedAt using UTC for role Admin
    And the authenticated user has roles Admin
    And the product catalog is empty
    When I create the following products through POST /crud
      | Name        | Description   | Price | Currency |
      | Aurora Lamp | Desk lighting | 49.99 | USD      |
    And I delete the following products through DELETE /crud
      | Name        |
      | Aurora Lamp |
    Then the EF Core store marks the following products as deleted
      | Name        |
      | Aurora Lamp |
    And the EF Core store sets DeletedAt in UTC for the following products
      | Name        |
      | Aurora Lamp |

  @regression @products
  Scenario: Soft delete records deletion time using local time
    Given the Product policy uses soft delete flag Deleted and timestamp DeletedAt using local time for role Admin
    And the authenticated user has roles Admin
    And the product catalog is empty
    When I create the following products through POST /crud
      | Name        | Description   | Price | Currency |
      | Aurora Lamp | Desk lighting | 49.99 | USD      |
    And I delete the following products through DELETE /crud
      | Name        |
      | Aurora Lamp |
    Then the EF Core store sets DeletedAt in local time for the following products
      | Name        |
      | Aurora Lamp |
