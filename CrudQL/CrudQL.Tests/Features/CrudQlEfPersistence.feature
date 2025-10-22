Feature: CRUD over EF Core persistence
  Scenario: CRUD operations persist through EF Core
    Given the product catalog is empty
    When I create the following products through POST /crud
      | Name       | Description       | Price | Currency |
      | Keyboard   | Mechanical 60%    | 450   | BRL      |
      | Headphones | Noise cancelling  | 899   | BRL      |
      | Laptop     | Ultrabook 14      | 7200  | BRL      |
    And I update the following products through PUT /crud
      | Name       | NewDescription  | NewPrice |
      | Keyboard   | Low profile 60% | 430      |
      | Headphones | ANC over-ear    | 879      |
    And I GET /crud for Product selecting id,name,description,price,currency
    Then the response contains the updated products
      | Name       | Description      | Price |
      | Keyboard   | Low profile 60%  | 430   |
      | Headphones | ANC over-ear     | 879   |
    And the remaining products keep their original description and price
    And each update response reports 1 affected row
    And the EF Core store matches the response payload
