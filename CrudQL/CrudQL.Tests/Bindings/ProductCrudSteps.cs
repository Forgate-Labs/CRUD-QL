using System;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;
using CrudQL.Service.DependencyInjection;
using CrudQL.Service.Routing;
using CrudQL.Tests.TestAssets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Reqnroll;

namespace CrudQL.Tests.Bindings;

[Binding]
public sealed class ProductCrudSteps : IDisposable
{
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly Dictionary<string, TrackedProduct> products = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> updated = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> deleted = new(StringComparer.OrdinalIgnoreCase);
    private WebApplicationFactory<ProductCrudTestProgram>? factory;
    private HttpClient? client;
    private IReadOnlyList<ProductDocument>? lastProducts;

    [Given("the product catalog is empty")]
    public void GivenTheProductCatalogIsEmpty()
    {
        Dispose();
        factory = new ProductCrudApplicationFactory();
        client = factory.CreateClient();
        products.Clear();
        updated.Clear();
        deleted.Clear();
        lastProducts = null;
    }

    [When("I create the following products through POST /products")]
    public async Task WhenICreateTheFollowingProductsThroughPostProducts(Table table)
    {
        Assert.That(client, Is.Not.Null);
        foreach (var row in table.Rows)
        {
            var request = new CreateProductRequest(
                row["Name"],
                row["Description"],
                decimal.Parse(row["Price"], CultureInfo.InvariantCulture),
                row["Currency"]);
            var response = await client!.PostAsJsonAsync("/products", request, jsonOptions);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            var product = await response.Content.ReadFromJsonAsync<ProductDocument>(jsonOptions);
            Assert.That(product, Is.Not.Null);
            products[product!.Name] = new TrackedProduct(
                product.Id,
                product.Name,
                product.Description,
                product.Price,
                product.Currency);
        }
    }

    [When("I GET /products")]
    public async Task WhenIGetProducts()
    {
        Assert.That(client, Is.Not.Null);
        var response = await client!.GetAsync("/products");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var collection = await response.Content.ReadFromJsonAsync<List<ProductDocument>>(jsonOptions);
        Assert.That(collection, Is.Not.Null);
        lastProducts = collection!;
    }

    [Then("the response contains 10 products with the same names in any order")]
    public void ThenTheResponseContainsTenProductsWithTheSameNamesInAnyOrder()
    {
        Assert.That(lastProducts, Is.Not.Null);
        Assert.That(lastProducts!, Has.Count.EqualTo(10));
        var actualNames = lastProducts!.Select(p => p.Name).ToList();
        Assert.That(actualNames, Is.EquivalentTo(products.Keys));
    }

    [When("I update the following products through PUT /products/{id}")]
    public async Task WhenIUpdateTheFollowingProductsThroughPutProductsId(Table table)
    {
        Assert.That(client, Is.Not.Null);
        foreach (var row in table.Rows)
        {
            var name = row["Name"];
            Assert.That(products.TryGetValue(name, out var tracked), Is.True);
            var payload = new UpdateProductRequest(
                row["NewDescription"],
                decimal.Parse(row["NewPrice"], CultureInfo.InvariantCulture));
            var response = await client!.PutAsJsonAsync($"/products/{tracked!.Id}", payload, jsonOptions);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var product = await response.Content.ReadFromJsonAsync<ProductDocument>(jsonOptions);
            Assert.That(product, Is.Not.Null);
            tracked.ApplyUpdate(product!.Description, product.Price);
            updated.Add(name);
        }
    }

    [Then("the response contains the updated products")]
    public void ThenTheResponseContainsTheUpdatedProducts(Table table)
    {
        Assert.That(lastProducts, Is.Not.Null);
        foreach (var row in table.Rows)
        {
            var name = row["Name"];
            var match = lastProducts!.SingleOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            Assert.That(match, Is.Not.Null);
            Assert.That(match!.Description, Is.EqualTo(row["Description"]));
            Assert.That(match.Price, Is.EqualTo(decimal.Parse(row["Price"], CultureInfo.InvariantCulture)));
        }
    }

    [Then("the remaining products keep their original description and price")]
    public void ThenTheRemainingProductsKeepTheirOriginalDescriptionAndPrice()
    {
        Assert.That(lastProducts, Is.Not.Null);
        foreach (var tracked in products.Values)
        {
            if (updated.Contains(tracked.Name) || deleted.Contains(tracked.Name))
            {
                continue;
            }

            var match = lastProducts!.Single(p => string.Equals(p.Name, tracked.Name, StringComparison.OrdinalIgnoreCase));
            Assert.That(match.Description, Is.EqualTo(tracked.OriginalDescription));
            Assert.That(match.Price, Is.EqualTo(tracked.OriginalPrice));
        }
    }

    [When("I delete the following products through DELETE /products/{id}")]
    public async Task WhenIDeleteTheFollowingProductsThroughDeleteProductsId(Table table)
    {
        Assert.That(client, Is.Not.Null);
        foreach (var row in table.Rows)
        {
            var name = row["Name"];
            Assert.That(products.TryGetValue(name, out var tracked), Is.True);
            var response = await client!.DeleteAsync($"/products/{tracked!.Id}");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            deleted.Add(name);
            products.Remove(name);
        }
    }

    [Then("the response contains 7 products")]
    public void ThenTheResponseContainsSevenProducts()
    {
        Assert.That(lastProducts, Is.Not.Null);
        Assert.That(lastProducts!, Has.Count.EqualTo(7));
    }

    [Then("the response does not include the deleted product names")]
    public void ThenTheResponseDoesNotIncludeTheDeletedProductNames()
    {
        Assert.That(lastProducts, Is.Not.Null);
        var names = lastProducts!.Select(p => p.Name).ToList();
        foreach (var name in deleted)
        {
            Assert.That(names, Does.Not.Contain(name));
        }
    }

    public void Dispose()
    {
        client?.Dispose();
        factory?.Dispose();
        client = null;
        factory = null;
    }

    private sealed class ProductCrudApplicationFactory : WebApplicationFactory<ProductCrudTestProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddDbContext<FakeDbContext>(options => options.UseInMemoryDatabase($"Products_{Guid.NewGuid()}"));
                services.AddCrudQl().AddEntitiesFromDbContext<FakeDbContext>();
            });

            builder.Configure(app =>
            {
                app.MapCrudQl();
            });
        }
    }

    private sealed class TrackedProduct
    {
        public TrackedProduct(int id, string name, string description, decimal price, string currency)
        {
            Id = id;
            Name = name;
            OriginalDescription = description;
            OriginalPrice = price;
            Currency = currency;
            CurrentDescription = description;
            CurrentPrice = price;
        }

        public int Id { get; }
        public string Name { get; }
        public string OriginalDescription { get; }
        public decimal OriginalPrice { get; }
        public string Currency { get; }
        public string CurrentDescription { get; private set; }
        public decimal CurrentPrice { get; private set; }

        public void ApplyUpdate(string description, decimal price)
        {
            CurrentDescription = description;
            CurrentPrice = price;
        }
    }

    private sealed record CreateProductRequest(string Name, string Description, decimal Price, string Currency);

    private sealed record UpdateProductRequest(string Description, decimal Price);

    private sealed record ProductDocument(int Id, string Name, string Description, decimal Price, string Currency);
}

public sealed class ProductCrudTestProgram
{
}
