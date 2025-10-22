Feature: Product authorization

  Scenario: Admin role can update products
    Given the Product policy allows only Admin for CRUD actions
    And the authenticated user has roles Admin
    And the product catalog is empty
    When I create the following products through POST /crud
      | Name     | Description      | Price | Currency |
      | Keyboard | Mechanical 60%   | 450   | BRL      |
    And I update the following products through PUT /crud
      | Name     | NewDescription  | NewPrice |
      | Keyboard | Low profile 60% | 430      |
    And I GET /crud for Product selecting id,name,description,price,currency
    Then the response contains the updated products
      | Name     | Description      | Price |
      | Keyboard | Low profile 60%  | 430   |
    And each update response reports 1 affected row

  Scenario: Non-admin role is blocked from updating
    Given the Product policy allows only Admin for CRUD actions
    And the authenticated user has roles Admin
    And the product catalog is empty
    When I create the following products through POST /crud
      | Name     | Description      | Price | Currency |
      | Keyboard | Mechanical 60%   | 450   | BRL      |
    And the authenticated user has roles Support
    And I attempt to update the following products through PUT /crud expecting Unauthorized
      | Name     | NewDescription  | NewPrice |
      | Keyboard | Low profile 60% | 430      |
    Then the last response status is Unauthorized
    And the authenticated user has roles Admin
    When I GET /crud for Product selecting id,name,description,price,currency
    Then the remaining products keep their original description and price

  Scenario: Admin role can create products
    Given the Product policy allows only Admin for CRUD actions
    And the authenticated user has roles Admin
    And the product catalog is empty
    When I create the following products through POST /crud
      | Name     | Description    | Price | Currency |
      | Keyboard | Mechanical 60% | 450   | BRL      |
      | Mouse    | Wireless       | 199   | BRL      |
    And I GET /crud for Product selecting id,name,description,price,currency
    Then the response contains the updated products
      | Name     | Description    | Price |
      | Keyboard | Mechanical 60% | 450   |
      | Mouse    | Wireless       | 199   |

  Scenario: Non-admin role is blocked from creating
    Given the Product policy allows only Admin for CRUD actions
    And the authenticated user has roles Support
    And the product catalog is empty
    When I attempt to create the following products through POST /crud expecting Unauthorized
      | Name     | Description    | Price | Currency |
      | Keyboard | Mechanical 60% | 450   | BRL      |
    Then the last response status is Unauthorized
    And the authenticated user has roles Admin
    When I GET /crud for Product selecting id,name,description,price,currency
    Then the response contains 0 products with the same names in any order

  Scenario: Admin role can read products
    Given the Product policy allows only Admin for CRUD actions
    And the authenticated user has roles Admin
    And the product catalog is empty
    When I create the following products through POST /crud
      | Name     | Description    | Price | Currency |
      | Keyboard | Mechanical 60% | 450   | BRL      |
    And I GET /crud for Product selecting id,name,description,price,currency
    Then the response contains the updated products
      | Name     | Description    | Price |
      | Keyboard | Mechanical 60% | 450   |

  Scenario: Non-admin role is blocked from reading
    Given the Product policy allows only Admin for CRUD actions
    And the authenticated user has roles Admin
    And the product catalog is empty
    When I create the following products through POST /crud
      | Name     | Description    | Price | Currency |
      | Keyboard | Mechanical 60% | 450   | BRL      |
    And the authenticated user has roles Support
    When I attempt to read products through GET /crud expecting Unauthorized selecting id,name,description,price,currency
    Then the last response status is Unauthorized
    And the authenticated user has roles Admin
    When I GET /crud for Product selecting id,name,description,price,currency
    Then the response contains the updated products
      | Name     | Description    | Price |
      | Keyboard | Mechanical 60% | 450   |

  Scenario: Admin role can delete products
    Given the Product policy allows only Admin for CRUD actions
    And the authenticated user has roles Admin
    And the product catalog is empty
    When I create the following products through POST /crud
      | Name     | Description    | Price | Currency |
      | Keyboard | Mechanical 60% | 450   | BRL      |
      | Mouse    | Wireless       | 199   | BRL      |
    And I delete the following products through DELETE /crud
      | Name |
      | Mouse |
    And I GET /crud for Product selecting id,name,description,price,currency
    Then the response contains the updated products
      | Name     | Description    | Price |
      | Keyboard | Mechanical 60% | 450   |

  Scenario: Non-admin role is blocked from deleting
    Given the Product policy allows only Admin for CRUD actions
    And the authenticated user has roles Admin
    And the product catalog is empty
    When I create the following products through POST /crud
      | Name     | Description    | Price | Currency |
      | Keyboard | Mechanical 60% | 450   | BRL      |
    And the authenticated user has roles Support
    When I attempt to delete the following products through DELETE /crud expecting Unauthorized
      | Name     |
      | Keyboard |
    Then the last response status is Unauthorized
    And the authenticated user has roles Admin
    When I GET /crud for Product selecting id,name,description,price,currency
    Then the response contains the updated products
      | Name     | Description    | Price |
      | Keyboard | Mechanical 60% | 450   |
