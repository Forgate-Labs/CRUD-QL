Feature: GET Ordering
  As an API consumer
  I want to order query results by specific fields
  So that I can retrieve data in the desired sequence

  Background:
    Given the product catalog is empty
    And the authenticated user has roles catalog
    When I create the following products through POST /crud
      | Name       | Description | Price  | Currency |
      | Product C  | Item C      | 30.00  | USD      |
      | Product A  | Item A      | 10.00  | USD      |
      | Product E  | Item E      | 50.00  | USD      |
      | Product B  | Item B      | 20.00  | USD      |
      | Product D  | Item D      | 40.00  | USD      |

  Scenario: Order by single field ascending
    When I send a GET request to "/crud?entity=Product&orderBy=name&select=id,name,price"
    Then the response status code should be 200
    And the response data should contain 5 products
    And the first product should have name "Product A"
    And the product at position 5 should have name "Product E"

  Scenario: Order by single field descending
    When I send a GET request to "/crud?entity=Product&orderBy=price:desc&select=id,name,price"
    Then the response status code should be 200
    And the response data should contain 5 products
    And the first product should have name "Product E"
    And the product at position 5 should have name "Product A"

  Scenario: Order by multiple fields
    Given I create the following products through POST /crud
      | Name       | Description | Price  | Currency |
      | Product F  | Category A  | 10.00  | USD      |
      | Product G  | Category B  | 10.00  | USD      |
    When I send a GET request to "/crud?entity=Product&orderBy=price:asc,name:desc&select=id,name,price"
    Then the response status code should be 200
    And the first product should have name "Product G"

  Scenario: Order with pagination
    When I send a GET request to "/crud?entity=Product&orderBy=price:asc&page=1&pageSize=2&select=id,name,price"
    Then the response status code should be 200
    And the response data should contain 2 products
    And the first product should have name "Product A"
    And the second product should have name "Product B"
    And the response pagination should have page 1
    And the response pagination should have hasNextPage true

  Scenario: Order with filter
    When I send a GET request to "/crud?entity=Product&orderBy=name:desc&filter={\"field\":\"price\",\"op\":\"gte\",\"value\":30}&select=id,name,price"
    Then the response status code should be 200
    And the response data should contain 3 products
    And the first product should have name "Product E"
    And the product at position 3 should have name "Product C"

  Scenario: Order by field not allowed returns error
    Given the Product entity has ordering configured with allowed fields price
    When I send a GET request to "/crud?entity=Product&orderBy=name&select=id,name,price"
    Then the response status code should be 400
    And the response should contain error message "Ordering by field 'name' is not allowed"

  Scenario: Order by allowed field succeeds
    Given the Product entity has ordering configured with allowed fields price,name
    When I send a GET request to "/crud?entity=Product&orderBy=price:desc&select=id,name,price"
    Then the response status code should be 200
    And the first product should have name "Product E"

  Scenario: Invalid order direction returns error
    When I send a GET request to "/crud?entity=Product&orderBy=price:invalid"
    Then the response status code should be 400
    And the response should contain error message "Invalid order direction 'invalid'"

  Scenario: Default ordering applied when not specified
    Given the Product entity has default ordering by price descending
    When I send a GET request to "/crud?entity=Product&select=id,name,price"
    Then the response status code should be 200
    And the first product should have name "Product E"
    And the product at position 5 should have name "Product A"

  Scenario: Explicit ordering overrides default
    Given the Product entity has default ordering by price descending
    When I send a GET request to "/crud?entity=Product&orderBy=name:asc&select=id,name,price"
    Then the response status code should be 200
    And the first product should have name "Product A"

  Scenario: Request without ordering and no default
    When I send a GET request to "/crud?entity=Product&select=id,name,price"
    Then the response status code should be 200
    And the response data should contain 5 products
