using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using CrudQL.Service.Authorization;
using CrudQL.Service.DependencyInjection;
using CrudQL.Service.Routing;
using CrudQL.Tests.TestAssets;
using FluentValidation;
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
    private readonly List<int> updateResponses = new();
    private readonly List<(IValidator<Product> Validator, CrudAction[] Actions)> validatorRegistrations = new();
    private TestServer? server;
    private HttpClient? client;
    private IReadOnlyList<ProductRecord>? lastProducts;
    private string? databaseName;
    private ICrudPolicy<Product>? productPolicy;
    private ClaimsPrincipal? currentUser;
    private HttpStatusCode? lastStatusCode;

    [Given("the Product policy allows only (.+) for CRUD actions")]
    public void GivenTheProductPolicyAllowsOnlyRolesForCrudActions(string roles)
    {
        var parsed = ParseRoles(roles);
        productPolicy = new RoleRestrictedProductPolicy(parsed);
    }

    [Given("the Product create validator requires name and positive price")]
    public void GivenTheProductCreateValidatorRequiresNameAndPositivePrice()
    {
        var validator = new InlineValidator<Product>();
        validator.RuleFor(x => x.Name).NotEmpty();
        validator.RuleFor(x => x.Price).GreaterThan(0);
        validatorRegistrations.Add((validator, new[] { CrudAction.Create }));
    }

    [Given("the Product update validator requires positive price")]
    public void GivenTheProductUpdateValidatorRequiresPositivePrice()
    {
        var validator = new InlineValidator<Product>();
        validator.RuleFor(x => x.Price).GreaterThan(0);
        validatorRegistrations.Add((validator, new[] { CrudAction.Update }));
    }

    [Given("the Product delete validator only allows deleting products cheaper than (.+)")]
    public void GivenTheProductDeleteValidatorOnlyAllowsDeletingProductsCheaperThan(string threshold)
    {
        var limit = decimal.Parse(threshold, CultureInfo.InvariantCulture);
        var validator = new InlineValidator<Product>();
        validator.RuleFor(x => x.Price).LessThan(limit);
        validatorRegistrations.Add((validator, new[] { CrudAction.Delete }));
    }

    [Given("the authenticated user has roles (.+)")]
    [When("the authenticated user has roles (.+)")]
    [Then("the authenticated user has roles (.+)")]
    public void GivenTheAuthenticatedUserHasRoles(string roles)
    {
        var parsed = ParseRoles(roles);
        var identity = new ClaimsIdentity(parsed.Length > 0 ? "TestAuth" : null);
        foreach (var role in parsed)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        currentUser = new ClaimsPrincipal(identity);
    }

    [Given("the product catalog is empty")]
    public void GivenTheProductCatalogIsEmpty()
    {
        Dispose();
        var hostBuilder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddRouting();
                databaseName = $"Products_{Guid.NewGuid()}";
                services.AddDbContext<FakeDbContext>(options => options.UseInMemoryDatabase(databaseName));
                services.AddCrudQl()
                    .AddEntity<Product>(cfg =>
                    {
                        if (productPolicy != null)
                        {
                            cfg.UsePolicy(productPolicy);
                        }

                        foreach (var (validator, actions) in validatorRegistrations)
                        {
                            cfg.UseValidator(validator, actions);
                        }
                    })
                    .AddEntitiesFromDbContext<FakeDbContext>();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.Use(async (context, next) =>
                {
                    context.User = currentUser ?? new ClaimsPrincipal(new ClaimsIdentity());
                    await next();
                });
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
        updateResponses.Clear();
        lastProducts = null;
        lastStatusCode = null;
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

    [When(@"I attempt to create the following products through POST \/crud expecting (.+)")]
    public async Task WhenIAttemptToCreateTheFollowingProductsThroughPostCrudExpecting(string status, Table table)
    {
        var expected = ParseStatusCode(status);
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
            lastStatusCode = response.StatusCode;
            Assert.That(response.StatusCode, Is.EqualTo(expected));
        }
    }

    [When(@"I GET \/crud for Product")]
    [Then(@"I GET \/crud for Product")]
    public async Task WhenIGetCrudForProduct()
    {
        await WhenIGetCrudForProductSelecting("id,name,description,price,currency");
    }

    [When(@"I GET \/crud for Product selecting (.+)")]
    [Then(@"I GET \/crud for Product selecting (.+)")]
    public async Task WhenIGetCrudForProductSelecting(string fields)
    {
        var currentClient = EnsureClient();
        var selectFields = fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var query = string.Join("&", selectFields.Select(field => $"select={Uri.EscapeDataString(field)}"));
        var response = await currentClient.GetAsync($"/crud?entity=Product&{query}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var collection = await response.Content.ReadFromJsonAsync<CollectionResponse>(jsonOptions);
        Assert.That(collection, Is.Not.Null);
        lastProducts = collection!.Data;
    }

    [When(@"I attempt to read products through GET \/crud expecting (.+) selecting (.+)")]
    public async Task WhenIAttemptToReadProductsThroughGetCrudExpecting(string status, string fields)
    {
        var expected = ParseStatusCode(status);
        var currentClient = EnsureClient();
        var selectFields = fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var query = string.Join("&", selectFields.Select(field => $"select={Uri.EscapeDataString(field)}"));
        var response = await currentClient.GetAsync($"/crud?entity=Product&{query}");
        lastStatusCode = response.StatusCode;
        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }

    [Then("the response contains 10 products with the same names in any order")]
    public void ThenTheResponseContainsTenProductsWithTheSameNamesInAnyOrder()
    {
        Assert.That(lastProducts, Is.Not.Null);
        Assert.That(lastProducts!, Has.Count.EqualTo(10));
        var actualNames = lastProducts!.Select(product => product.Name).ToList();
        Assert.That(actualNames, Is.EquivalentTo(products.Keys));
    }

    [Then("the response contains 0 products with the same names in any order")]
    public void ThenTheResponseContainsZeroProductsWithTheSameNamesInAnyOrder()
    {
        Assert.That(lastProducts, Is.Not.Null);
        Assert.That(lastProducts!, Has.Count.EqualTo(0));
    }

    [When(@"I update the following products through PUT \/crud")]
    public async Task WhenIUpdateTheFollowingProductsThroughPutCrud(Table table)
    {
        var currentClient = EnsureClient();
        foreach (var row in table.Rows)
        {
            var name = row["Name"];
            Assert.That(products.TryGetValue(name, out var tracked), Is.True);
            var newDescription = row["NewDescription"];
            var newPrice = decimal.Parse(row["NewPrice"], CultureInfo.InvariantCulture);
            var payload = new UpdatePayload(
                "Product",
                new ConditionPayload("id", "eq", tracked!.Id),
                new UpdateChanges(
                    newDescription,
                    newPrice));
            var message = new HttpRequestMessage(HttpMethod.Put, "/crud")
            {
                Content = JsonContent.Create(payload, options: jsonOptions)
            };
            var response = await currentClient.SendAsync(message);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            lastStatusCode = response.StatusCode;
            var document = await response.Content.ReadFromJsonAsync<UpdateResponse>(jsonOptions);
            Assert.That(document, Is.Not.Null);
            Assert.That(document!.AffectedRows, Is.EqualTo(1));
            updateResponses.Add(document.AffectedRows);
            tracked.ApplyUpdate(newDescription, newPrice);
            updated.Add(name);
        }
    }

    [When(@"I attempt to update the following products through PUT \/crud expecting (.+)")]
    public async Task WhenIAttemptToUpdateTheFollowingProductsThroughPutCrudExpecting(string status, Table table)
    {
        var expected = ParseStatusCode(status);
        var currentClient = EnsureClient();
        foreach (var row in table.Rows)
        {
            var name = row["Name"];
            Assert.That(products.TryGetValue(name, out var tracked), Is.True);
            var payload = new UpdatePayload(
                "Product",
                new ConditionPayload("id", "eq", tracked!.Id),
                new UpdateChanges(
                    row["NewDescription"],
                    decimal.Parse(row["NewPrice"], CultureInfo.InvariantCulture)));
            var message = new HttpRequestMessage(HttpMethod.Put, "/crud")
            {
                Content = JsonContent.Create(payload, options: jsonOptions)
            };
            var response = await currentClient.SendAsync(message);
            lastStatusCode = response.StatusCode;
            Assert.That(response.StatusCode, Is.EqualTo(expected));
        }
    }

    [When(@"I attempt to delete the following products through DELETE \/crud expecting (.+)")]
    public async Task WhenIAttemptToDeleteTheFollowingProductsThroughDeleteCrudExpecting(string status, Table table)
    {
        var expected = ParseStatusCode(status);
        var currentClient = EnsureClient();
        foreach (var row in table.Rows)
        {
            var name = row["Name"];
            Assert.That(products.TryGetValue(name, out var tracked), Is.True);
            var response = await currentClient.DeleteAsync($"/crud?entity=Product&id={tracked!.Id}");
            lastStatusCode = response.StatusCode;
            Assert.That(response.StatusCode, Is.EqualTo(expected));
        }
    }

    [Then("each update response reports 1 affected row")]
    public void ThenEachUpdateResponseReportsOneAffectedRow()
    {
        Assert.That(updateResponses, Is.Not.Empty);
        Assert.That(updateResponses, Has.All.EqualTo(1));
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

    [Then("the last response status is (.+)")]
    public void ThenTheLastResponseStatusIs(string status)
    {
        Assert.That(lastStatusCode, Is.Not.Null);
        var expected = ParseStatusCode(status);
        Assert.That(lastStatusCode, Is.EqualTo(expected));
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

    [Then("the EF Core store matches the response payload")]
    public void ThenTheEfCoreStoreMatchesTheResponsePayload()
    {
        Assert.That(lastProducts, Is.Not.Null);
        using var scope = server!.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FakeDbContext>();
        var records = dbContext.Products
            .AsNoTracking()
            .Select(product => new ProductRecord(product.Id, product.Name, product.Description, product.Price, product.Currency))
            .ToList();
        Assert.That(records, Has.Count.EqualTo(lastProducts!.Count));
        foreach (var product in lastProducts!)
        {
            var match = records.SingleOrDefault(record => record.Id == product.Id);
            Assert.That(match, Is.Not.Null, $"Product with id {product.Id} not found in EF Core store");
            Assert.That(match!.Name, Is.EqualTo(product.Name));
            Assert.That(match.Description, Is.EqualTo(product.Description));
            Assert.That(match.Price, Is.EqualTo(product.Price));
            Assert.That(match.Currency, Is.EqualTo(product.Currency));
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

    private static string[] ParseRoles(string roles)
    {
        if (string.IsNullOrWhiteSpace(roles))
        {
            return Array.Empty<string>();
        }

        return roles
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(role => !string.Equals(role, "none", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static HttpStatusCode ParseStatusCode(string value)
    {
        if (Enum.TryParse<HttpStatusCode>(value, true, out var status))
        {
            return status;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric) &&
            Enum.IsDefined(typeof(HttpStatusCode), numeric))
        {
            return (HttpStatusCode)numeric;
        }

        throw new ArgumentException($"Unknown HTTP status '{value}'", nameof(value));
    }

    private sealed class RoleRestrictedProductPolicy : CrudPolicy<Product>
    {
        public RoleRestrictedProductPolicy(IReadOnlyCollection<string> roles)
        {
            if (roles.Count == 0)
            {
                throw new ArgumentException("At least one role must be provided.", nameof(roles));
            }

            var roleArray = roles.ToArray();
            foreach (var action in Enum.GetValues<CrudAction>())
            {
                Allow(action).ForRoles(roleArray);
            }
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

    private sealed record ProductRecord(int Id, string Name, string Description, decimal Price, string Currency);

    private sealed record SingleResponse(ProductRecord Data);

    private sealed record CollectionResponse(List<ProductRecord> Data);

    private sealed record ProductInput(string Name, string Description, decimal Price, string Currency);

    private sealed record CreatePayload(string Entity, ProductInput Input, string[] Returning);

    private sealed record UpdateChanges(string Description, decimal Price);

    private sealed record ConditionPayload(string Field, string Op, object Value);

    private sealed record UpdatePayload(string Entity, ConditionPayload Condition, UpdateChanges Update);

    private sealed record UpdateResponse(int AffectedRows);

}
