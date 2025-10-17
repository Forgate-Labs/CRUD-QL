Feature: Product CRUD lifecycle

  @regression @products
  Scenario: CRUD lifecycle for multiple products
    Given the product catalog is empty
    When I create the following products through POST /crud
      | Name           | Description           | Price | Currency |
      | Aurora Lamp    | Desk lighting         | 49.99 | USD      |
      | Borealis Chair | Ergonomic seating     | 129.0 | USD      |
      | Comet Mug      | Insulated beverage    | 19.5  | USD      |
      | Drift Blanket  | Merino wool throw     | 89.0  | USD      |
      | Equinox Watch  | Stainless steel watch | 249.0 | USD      |
      | Flux Backpack  | Weatherproof pack     | 139.0 | USD      |
      | Glimmer Pen    | Signature fountain    | 34.0  | USD      |
      | Halo Speaker   | Smart home audio      | 159.0 | USD      |
      | Ion Bottle     | Vacuum water bottle   | 27.5  | USD      |
      | Jolt Charger   | 65W USB-C charger     | 42.0  | USD      |
    And I GET /crud for Product
    Then the response contains 10 products with the same names in any order
    When I update the following products through PUT /crud specifying update fields id,name,description,price,currency
      | Name          | NewDescription        | NewPrice |
      | Equinox Watch | Sapphire crystal case | 279.0    |
      | Flux Backpack | Added laptop sleeve   | 149.0    |
    And I GET /crud for Product
    Then the response contains the updated products
      | Name          | Description           | Price |
      | Equinox Watch | Sapphire crystal case | 279.0 |
      | Flux Backpack | Added laptop sleeve   | 149.0 |
    And the remaining products keep their original description and price
    When I delete the following products through DELETE /crud
      | Name        |
      | Aurora Lamp |
      | Comet Mug   |
      | Glimmer Pen |
    And I GET /crud for Product
    Then the response contains 7 products
    And the response does not include the deleted product names
