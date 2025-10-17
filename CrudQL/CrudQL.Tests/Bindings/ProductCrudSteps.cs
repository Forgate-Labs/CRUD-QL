using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CrudQL.Service.DependencyInjection;
using CrudQL.Service.Routing;
using CrudQL.Tests.TestAssets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
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
    private TestServer? server;
    private HttpClient? client;
    private IReadOnlyList<ProductRecord>? lastProducts;

    [Given("the product catalog is empty")]
    public void GivenTheProductCatalogIsEmpty()
    {
        Dispose();
        var hostBuilder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddRouting();
                services.AddDbContext<FakeDbContext>(options => options.UseInMemoryDatabase($"Products_{Guid.NewGuid()}"));
                services.AddCrudQl().AddEntitiesFromDbContext<FakeDbContext>();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapCrudQl();
                });
            });

        server = new TestServer(hostBuilder);
        client = server.CreateClient();
        products.Clear();
        updated.Clear();
        deleted.Clear();
        lastProducts = null;
    }

    [When(@"I create the following products through POST \/crud")]
    public async Task WhenICreateTheFollowingProductsThroughPostCrud(Table table)
    {
        var currentClient = EnsureClient();
        foreach (var row in table.Rows)
        {
            var payload = new CreatePayload(
                "Product",
                new ProductInput(
                    row["Name"],
                    row["Description"],
                    decimal.Parse(row["Price"], CultureInfo.InvariantCulture),
                    row["Currency"]),
                new[] { "id", "name", "description", "price", "currency" });
            var response = await currentClient.PostAsJsonAsync("/crud", payload, jsonOptions);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            var document = await response.Content.ReadFromJsonAsync<SingleResponse>(jsonOptions);
            Assert.That(document, Is.Not.Null);
            var record = document!.Data;
            products[record.Name] = new TrackedProduct(record.Id, record.Name, record.Description, record.Price, record.Currency);
        }
    }

    [When(@"I GET \/crud for Product")]
    public async Task WhenIGetCrudForProduct()
    {
        var currentClient = EnsureClient();
        var selectFields = new[] { "id", "name", "description", "price", "currency" };
        var query = string.Join("&", selectFields.Select(field => $"select={Uri.EscapeDataString(field)}"));
        var response = await currentClient.GetAsync($"/crud?entity=Product&{query}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var collection = await response.Content.ReadFromJsonAsync<CollectionResponse>(jsonOptions);
        Assert.That(collection, Is.Not.Null);
        lastProducts = collection!.Data;
    }

    [Then("the response contains 10 products with the same names in any order")]
    public void ThenTheResponseContainsTenProductsWithTheSameNamesInAnyOrder()
    {
        Assert.That(lastProducts, Is.Not.Null);
        Assert.That(lastProducts!, Has.Count.EqualTo(10));
        var actualNames = lastProducts!.Select(product => product.Name).ToList();
        Assert.That(actualNames, Is.EquivalentTo(products.Keys));
    }

    [When(@"I update the following products through PUT \/crud")]
    public async Task WhenIUpdateTheFollowingProductsThroughPutCrud(Table table)
    {
        var currentClient = EnsureClient();
        foreach (var row in table.Rows)
        {
            var name = row["Name"];
            Assert.That(products.TryGetValue(name, out var tracked), Is.True);
            var payload = new UpdatePayload(
                "Product",
                new ProductKey(tracked!.Id),
                new UpdateInput(
                    row["NewDescription"],
                    decimal.Parse(row["NewPrice"], CultureInfo.InvariantCulture)),
                new[] { "id", "name", "description", "price", "currency" });
            var message = new HttpRequestMessage(HttpMethod.Put, "/crud")
            {
                Content = JsonContent.Create(payload, options: jsonOptions)
            };
            var response = await currentClient.SendAsync(message);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var document = await response.Content.ReadFromJsonAsync<SingleResponse>(jsonOptions);
            Assert.That(document, Is.Not.Null);
            var record = document!.Data;
            tracked.ApplyUpdate(record.Description, record.Price);
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
            var match = lastProducts!.SingleOrDefault(product => string.Equals(product.Name, name, StringComparison.OrdinalIgnoreCase));
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

            var match = lastProducts!.Single(product => string.Equals(product.Name, tracked.Name, StringComparison.OrdinalIgnoreCase));
            Assert.That(match.Description, Is.EqualTo(tracked.OriginalDescription));
            Assert.That(match.Price, Is.EqualTo(tracked.OriginalPrice));
        }
    }

    [When(@"I delete the following products through DELETE \/crud")]
    public async Task WhenIDeleteTheFollowingProductsThroughDeleteCrud(Table table)
    {
        var currentClient = EnsureClient();
        foreach (var row in table.Rows)
        {
            var name = row["Name"];
            Assert.That(products.TryGetValue(name, out var tracked), Is.True);
            var response = await currentClient.DeleteAsync($"/crud?entity=Product&id={tracked!.Id}");
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
        var names = lastProducts!.Select(product => product.Name).ToList();
        foreach (var name in deleted)
        {
            Assert.That(names, Does.Not.Contain(name));
        }
    }

    public void Dispose()
    {
        client?.Dispose();
        server?.Dispose();
        client = null;
        server = null;
    }

    private HttpClient EnsureClient()
    {
        Assert.That(client, Is.Not.Null);
        return client!;
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

    private sealed record ProductRecord(int Id, string Name, string Description, decimal Price, string Currency);

    private sealed record SingleResponse(ProductRecord Data);

    private sealed record CollectionResponse(List<ProductRecord> Data);

    private sealed record ProductInput(string Name, string Description, decimal Price, string Currency);

    private sealed record ProductKey(int Id);

    private sealed record CreatePayload(string Entity, ProductInput Input, string[] Returning);

    private sealed record UpdateInput(string Description, decimal Price);

    private sealed record UpdatePayload(string Entity, ProductKey Key, UpdateInput Input, string[] Returning);

}
