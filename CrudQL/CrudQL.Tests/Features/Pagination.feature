Feature: GET Pagination
  As an API consumer
  I want to paginate large result sets
  So that I can efficiently retrieve data in manageable chunks

  Background:
    Given the product catalog is empty
    And the authenticated user has roles catalog
    When I create the following products through POST /crud
      | Name       | Description | Price  | Currency |
      | Product 1  | Item 1      | 10.00  | USD      |
      | Product 2  | Item 2      | 20.00  | USD      |
      | Product 3  | Item 3      | 30.00  | USD      |
      | Product 4  | Item 4      | 40.00  | USD      |
      | Product 5  | Item 5      | 50.00  | USD      |
      | Product 6  | Item 6      | 60.00  | USD      |
      | Product 7  | Item 7      | 70.00  | USD      |
      | Product 8  | Item 8      | 80.00  | USD      |
      | Product 9  | Item 9      | 90.00  | USD      |
      | Product 10 | Item 10     | 100.00 | USD      |

  Scenario: Retrieve first page with default page size
    When I send a GET request to "/crud?entity=Product&page=1&pageSize=3"
    Then the response status code should be 200
    And the response data should contain 3 products
    And the response pagination should have page 1
    And the response pagination should have pageSize 3
    And the response pagination should have hasNextPage true
    And the response pagination should have hasPreviousPage false

  Scenario: Retrieve second page
    When I send a GET request to "/crud?entity=Product&page=2&pageSize=3"
    Then the response status code should be 200
    And the response data should contain 3 products
    And the first product should have name "Product 4"
    And the response pagination should have page 2
    And the response pagination should have hasNextPage true
    And the response pagination should have hasPreviousPage true

  Scenario: Retrieve last page with partial results
    When I send a GET request to "/crud?entity=Product&page=4&pageSize=3"
    Then the response status code should be 200
    And the response data should contain 1 products
    And the first product should have name "Product 10"
    And the response pagination should have page 4
    And the response pagination should have hasNextPage false
    And the response pagination should have hasPreviousPage true

  Scenario: Retrieve page beyond available data
    When I send a GET request to "/crud?entity=Product&page=10&pageSize=3"
    Then the response status code should be 200
    And the response data should contain 0 products
    And the response pagination should have page 10
    And the response pagination should have hasNextPage false
    And the response pagination should have hasPreviousPage true

  Scenario: Request with includeCount returns total records
    When I send a GET request to "/crud?entity=Product&page=1&pageSize=3&includeCount=true"
    Then the response status code should be 200
    And the response data should contain 3 products
    And the response pagination should have totalRecords 10
    And the response pagination should have totalPages 4

  Scenario: Request without includeCount omits total records
    When I send a GET request to "/crud?entity=Product&page=1&pageSize=3"
    Then the response status code should be 200
    And the response pagination should not have totalRecords
    And the response pagination should not have totalPages

  Scenario: Pagination with filtering
    When I send a GET request to "/crud?entity=Product&page=1&pageSize=2&filter={\"field\":\"price\",\"op\":\"gte\",\"value\":50}"
    Then the response status code should be 200
    And the response data should contain 2 products
    And the first product should have name "Product 5"
    And the second product should have name "Product 6"
    And the response pagination should have hasNextPage true

  Scenario: Pagination with field selection
    When I send a GET request to "/crud?entity=Product&page=1&pageSize=2&select=id,name"
    Then the response status code should be 200
    And the response data should contain 2 products
    And each product should only have fields "id,name"

  Scenario: Invalid page number (zero)
    When I send a GET request to "/crud?entity=Product&page=0&pageSize=3"
    Then the response status code should be 400
    And the response should contain error message "Page must be greater than or equal to 1"

  Scenario: Invalid page number (negative)
    When I send a GET request to "/crud?entity=Product&page=-1&pageSize=3"
    Then the response status code should be 400
    And the response should contain error message "Page must be greater than or equal to 1"

  Scenario: Invalid page size (zero)
    When I send a GET request to "/crud?entity=Product&page=1&pageSize=0"
    Then the response status code should be 400
    And the response should contain error message "PageSize must be greater than or equal to 1"

  Scenario: Invalid page size (negative)
    When I send a GET request to "/crud?entity=Product&page=1&pageSize=-5"
    Then the response status code should be 400
    And the response should contain error message "PageSize must be greater than or equal to 1"

  Scenario: Page size exceeds maximum allowed
    Given the Product entity has max page size configured as 100
    When I send a GET request to "/crud?entity=Product&page=1&pageSize=500"
    Then the response status code should be 400
    And the response should contain error message "PageSize cannot exceed 100"

  Scenario: Request without pagination returns all results
    When I send a GET request to "/crud?entity=Product"
    Then the response status code should be 200
    And the response data should contain 10 products
    And the response should not have pagination metadata

  Scenario: Default page size is applied when not specified
    Given the Product entity has default page size configured as 5
    When I send a GET request to "/crud?entity=Product&page=1"
    Then the response status code should be 200
    And the response data should contain 5 products
    And the response pagination should have pageSize 5
