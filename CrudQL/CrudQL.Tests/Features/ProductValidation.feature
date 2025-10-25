Feature: Product validation

  Scenario: Create validator rejects invalid payload
    Given the Product create validator requires name and positive price
    And the product catalog is empty
    When I attempt to create the following products through POST /crud expecting BadRequest
      | Name | Description   | Price | Currency |
      |      | Missing name  | -10   | USD      |
    Then the last response status is BadRequest
    And I GET /crud for Product selecting id,name,description,price,currency
    Then the response contains 0 products with the same names in any order

  Scenario: Create validator accepts valid payload
    Given the Product create validator requires name and positive price
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

  Scenario: Create rejects unknown Product fields
    Given the product catalog is empty
    When I attempt to create a raw Product payload with unknown fields through POST /crud expecting BadRequest
      | field | value       |
      | title | Mouse Pro   |
      | genre | Accessories |
    Then the last response status is BadRequest
    And the last response reports unknown Product fields title,genre

  Scenario: Create rejects unknown Product fields even with a validator
    Given the Product create validator requires name and positive price
    And the product catalog is empty
    When I attempt to create a raw Product payload with unknown fields through POST /crud expecting BadRequest
      | field    | value      |
      | synopsis | Great item |
      | title    | Mouse Pro  |
    Then the last response status is BadRequest
    And the last response reports unknown Product fields synopsis,title

  Scenario: Update validator rejects negative price
    Given the Product update validator requires positive price
    And the product catalog is empty
    When I create the following products through POST /crud
      | Name     | Description    | Price | Currency |
      | Keyboard | Mechanical 60% | 450   | BRL      |
    And I attempt to update the following products through PUT /crud expecting BadRequest
      | Name     | NewDescription | NewPrice |
      | Keyboard | Discounted     | -5       |
    Then the last response status is BadRequest

  Scenario: Update validator accepts positive price
    Given the Product update validator requires positive price
    And the product catalog is empty
    When I create the following products through POST /crud
      | Name     | Description    | Price | Currency |
      | Keyboard | Mechanical 60% | 450   | BRL      |
    And I update the following products through PUT /crud
      | Name     | NewDescription | NewPrice |
      | Keyboard | Updated        | 500      |
    And I GET /crud for Product selecting id,name,description,price,currency
    Then the response contains the updated products
      | Name     | Description | Price |
      | Keyboard | Updated     | 500   |

  Scenario: Read validator rejects negative price filter
    Given the Product read filter validator requires non-negative price
    And the product catalog is empty
    When I create the following products through POST /crud
      | Name     | Description    | Price | Currency |
      | Keyboard | Mechanical 60% | 450   | BRL      |
    And I read products through GET /crud with filter expecting BadRequest
      | field | op  | value |
      | price | gte | -10   |
    Then the last response status is BadRequest

  Scenario: Read rejects unknown select fields from query string
    Given the product catalog is empty
    When I attempt to read products through GET /crud expecting BadRequest selecting foobar
    Then the last response status is BadRequest
    And the last response reports unknown Product fields foobar

  Scenario: Read rejects unknown select fields from payload
    Given the product catalog is empty
    When I attempt to read products through GET /crud with payload select expecting BadRequest selecting title
    Then the last response status is BadRequest
    And the last response reports unknown Product fields title

  Scenario: Read validator accepts non-negative price filter
    Given the Product read filter validator requires non-negative price
    And the product catalog is empty
    When I create the following products through POST /crud
      | Name     | Description    | Price | Currency |
      | Keyboard | Mechanical 60% | 450   | BRL      |
      | Mouse    | Wireless       | 199   | BRL      |
    And I read products through GET /crud with filter expecting OK
      | field | op  | value |
      | price | gte | 300   |
    Then the response contains the updated products
      | Name     | Description    | Price |
      | Keyboard | Mechanical 60% | 450   |

  Scenario: Delete validator blocks expensive product
    Given the Product delete validator only allows deleting products cheaper than 200
    And the product catalog is empty
    When I create the following products through POST /crud
      | Name     | Description    | Price | Currency |
      | Keyboard | Mechanical 60% | 450   | BRL      |
    And I attempt to delete the following products through DELETE /crud expecting BadRequest
      | Name     |
      | Keyboard |
    Then the last response status is BadRequest
    And I GET /crud for Product selecting id,name,description,price,currency
    Then the response contains the updated products
      | Name     | Description    | Price |
      | Keyboard | Mechanical 60% | 450   |

  Scenario: Delete validator allows affordable product
    Given the Product delete validator only allows deleting products cheaper than 500
    And the product catalog is empty
    When I create the following products through POST /crud
      | Name     | Description    | Price | Currency |
      | Mouse    | Wireless       | 199   | BRL      |
    And I delete the following products through DELETE /crud
      | Name  |
      | Mouse |
    And I GET /crud for Product selecting id,name,description,price,currency
    Then the response contains 0 products with the same names in any order
