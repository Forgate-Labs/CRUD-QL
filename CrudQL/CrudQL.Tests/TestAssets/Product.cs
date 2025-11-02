namespace CrudQL.Tests.TestAssets;

public class Product
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public string Currency { get; set; } = string.Empty;

    public int? CategoryId { get; set; }

    public Category? Category { get; set; }
}
