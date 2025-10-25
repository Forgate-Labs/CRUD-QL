Feature: Product projections

  Scenario: Support role sees masked fields when requesting unauthorized projections
    Given the Product policy restricts Support read and create responses to id and name with mask "####" while Admin remains unrestricted
    And the authenticated user has roles Admin
    And the product catalog is empty
    When I create the following products through POST /crud
      | Name     | Description    | Price | Currency |
      | Keyboard | Mechanical 60% | 450   | BRL      |
    And the authenticated user has roles Support
    When I GET /crud for Product selecting id,name,description,price,currency
    Then the last response masks the following Product fields with "####"
      | Name     | Description | Price | Currency |
      | Keyboard | ****        | ****  | ****     |
    And the authenticated user has roles Admin
    When I GET /crud for Product selecting id,name,description,price,currency
    Then the response contains the updated products
      | Name     | Description    | Price |
      | Keyboard | Mechanical 60% | 450   |

  Scenario: Support role receives masked values from create returning clause
    Given the Product policy restricts Support read and create responses to id and name with mask "!!!!" while Admin remains unrestricted
    And the authenticated user has roles Support
    And the product catalog is empty
    When I create a Product through POST /crud selecting id,name,description,price,currency
      | Name     | Description    | Price | Currency |
      | Keyboard | Mechanical 60% | 450   | BRL      |
    Then the last response masks the following Product fields with "!!!!"
      | Name     | Description | Price | Currency |
      | Keyboard | ****        | ****  | ****     |
    And the authenticated user has roles Admin
    When I GET /crud for Product selecting id,name,description,price,currency
    Then the response contains the updated products
      | Name     | Description    | Price |
      | Keyboard | Mechanical 60% | 450   |

  Scenario: Default suppression applies when no mask is configured
    Given the Product policy restricts Support read responses to id and name without configuring a mask
    And the authenticated user has roles Admin
    And the product catalog is empty
    When I create the following products through POST /crud
      | Name     | Description    | Price | Currency |
      | Keyboard | Mechanical 60% | 450   | BRL      |
    And the authenticated user has roles Support
    When I GET /crud for Product selecting id,name,description,price,currency
    Then the last response masks the following Product fields with "****"
      | Name     | Description | Price | Currency |
      | Keyboard | ****        | ****  | ****     |
