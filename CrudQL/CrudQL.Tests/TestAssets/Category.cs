using System.Collections.Generic;

namespace CrudQL.Tests.TestAssets;

public class Category
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
