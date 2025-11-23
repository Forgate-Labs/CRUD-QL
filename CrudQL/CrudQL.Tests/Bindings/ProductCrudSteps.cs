using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using CrudQL.Service.Authorization;
using CrudQL.Service.DependencyInjection;
using CrudQL.Service.Routing;
using CrudQL.Service.Validation;
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
    private readonly List<(IValidator Validator, CrudAction[] Actions, Type TargetType)> validatorRegistrations = new();
    private TestServer? server;
    private HttpClient? client;
    private IReadOnlyList<ProductRecord>? lastProducts;
    private string? databaseName;
    private ICrudPolicy<Product>? productPolicy;
    private ClaimsPrincipal? currentUser;
    private HttpStatusCode? lastStatusCode;
    private string? lastResponseBody;

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
        validatorRegistrations.Add((validator, new[] { CrudAction.Create }, typeof(Product)));
    }

    [Given("the Product update validator requires positive price")]
    public void GivenTheProductUpdateValidatorRequiresPositivePrice()
    {
        var validator = new InlineValidator<Product>();
        validator.RuleFor(x => x.Price).GreaterThan(0);
        validatorRegistrations.Add((validator, new[] { CrudAction.Update }, typeof(Product)));
    }

    [Given("the Product delete validator only allows deleting products cheaper than (.+)")]
    public void GivenTheProductDeleteValidatorOnlyAllowsDeletingProductsCheaperThan(string threshold)
    {
        var limit = decimal.Parse(threshold, CultureInfo.InvariantCulture);
        var validator = new InlineValidator<Product>();
        validator.RuleFor(x => x.Price).LessThan(limit);
        validatorRegistrations.Add((validator, new[] { CrudAction.Delete }, typeof(Product)));
    }

    [Given("the Product read filter validator requires non-negative price")]
    public void GivenTheProductReadFilterValidatorRequiresNonNegativePrice()
    {
        var validator = new InlineValidator<CrudFilterContext>();
        validator.RuleFor(x => x).Custom((model, context) =>
        {
            if (!model.Filter.HasValue)
            {
                return;
            }

            if (FilterContainsNegativePrice(model.Filter.Value))
            {
                context.AddFailure("filter", "Price filters must use non-negative values.");
            }
        });

        validatorRegistrations.Add((validator, new[] { CrudAction.Read }, typeof(CrudFilterContext)));
    }

    [Given(@"the Product policy restricts Support read and create responses to id and name with mask ""(.+)"" while Admin remains unrestricted")]
    public void GivenTheProductPolicyRestrictsSupportReadAndCreateResponses(string mask)
    {
        productPolicy = ProjectionRestrictedProductPolicy.ForSupport(mask, restrictCreate: true);
    }

    [Given("the Product policy restricts Support read responses to id and name without configuring a mask")]
    public void GivenTheProductPolicyRestrictsSupportReadResponsesWithoutMask()
    {
        productPolicy = ProjectionRestrictedProductPolicy.ForSupport(null, restrictCreate: false);
    }

    [Given("the Product policy uses soft delete flag (.+) for role (.+)")]
    public void GivenTheProductPolicyUsesSoftDeleteFlagForRole(string flagField, string role)
    {
        productPolicy = SoftDeleteProductPolicy.ForFlagOnly(flagField, role);
    }

    [Given(@"the Product policy uses soft delete flag (.+) and timestamp (.+) using UTC for role (.+)")]
    public void GivenTheProductPolicyUsesSoftDeleteFlagAndTimestampUsingUtcForRole(string flagField, string timestampField, string role)
    {
        productPolicy = SoftDeleteProductPolicy.ForTimestamp(flagField, timestampField, useUtc: true, role);
    }

    [Given(@"the Product policy uses soft delete flag (.+) and timestamp (.+) using local time for role (.+)")]
    public void GivenTheProductPolicyUsesSoftDeleteFlagAndTimestampUsingLocalTimeForRole(string flagField, string timestampField, string role)
    {
        productPolicy = SoftDeleteProductPolicy.ForTimestamp(flagField, timestampField, useUtc: false, role);
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

                        foreach (var (validator, actions, targetType) in validatorRegistrations)
                        {
                            if (targetType == typeof(Product))
                            {
                                cfg.UseValidator((IValidator<Product>)validator, actions);
                                continue;
                            }

                            if (targetType == typeof(CrudFilterContext))
                            {
                                cfg.UseFilterValidator((IValidator<CrudFilterContext>)validator);
                            }
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
        lastResponseBody = null;
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

    [When(@"I attempt to create a raw Product payload with unknown fields through POST \/crud expecting (.+)")]
    public async Task WhenIAttemptToCreateARawProductPayloadWithUnknownFieldsThroughPostCrudExpecting(string status, Table table)
    {
        var expected = ParseStatusCode(status);
        var currentClient = EnsureClient();
        var payload = new RawCreatePayload(
            "Product",
            BuildRawInput(table),
            new[] { "id", "name", "description", "price", "currency" });
        var response = await currentClient.PostAsJsonAsync("/crud", payload, jsonOptions);
        lastStatusCode = response.StatusCode;
        lastResponseBody = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(expected), $"Response payload: {lastResponseBody}");
    }

    [When(@"I create a Product through POST \/crud selecting (.+)")]
    public async Task WhenICreateAProductThroughPostCrudSelecting(string returning, Table table)
    {
        var selectFields = returning.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
                selectFields);
            var response = await currentClient.PostAsJsonAsync("/crud", payload, jsonOptions);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            lastStatusCode = response.StatusCode;
            lastResponseBody = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(lastResponseBody);
            var data = document.RootElement.GetProperty("data");
            var id = data.GetProperty("id").GetInt32();
            var name = row["Name"];
            products[name] = new TrackedProduct(
                id,
                name,
                row["Description"],
                decimal.Parse(row["Price"], CultureInfo.InvariantCulture),
                row["Currency"]);
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
        lastStatusCode = response.StatusCode;
        lastResponseBody = await response.Content.ReadAsStringAsync();
        var collection = TryDeserializeCollectionResponse(lastResponseBody);
        lastProducts = collection?.Data;
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
        lastResponseBody = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }

    [When(@"I attempt to read products through GET \/crud with payload select expecting (.+) selecting (.+)")]
    public async Task WhenIAttemptToReadProductsThroughGetCrudWithPayloadSelectExpecting(string status, string fields)
    {
        var expected = ParseStatusCode(status);
        var currentClient = EnsureClient();
        var selectFields = fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var payload = new ReadPayload("Product", null, selectFields);
        var message = new HttpRequestMessage(HttpMethod.Get, "/crud?entity=Product")
        {
            Content = JsonContent.Create(payload, options: jsonOptions)
        };
        var response = await currentClient.SendAsync(message);
        lastStatusCode = response.StatusCode;
        lastResponseBody = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(expected), $"Response payload: {lastResponseBody}");
        if (expected == HttpStatusCode.OK)
        {
            var collection = TryDeserializeCollectionResponse(lastResponseBody);
            lastProducts = collection?.Data;
        }
        else
        {
            lastProducts = null;
        }
    }

    [When("I read products through GET /crud with filter expecting (.+)")]
    public async Task WhenIReadProductsThroughGetCrudWithFilterExpecting(string status, Table table)
    {
        var expected = ParseStatusCode(status);
        var currentClient = EnsureClient();
        var filter = BuildFilterPayload(table);
        var payload = new ReadPayload(
            "Product",
            filter,
            new[] { "id", "name", "description", "price", "currency" });
        var message = new HttpRequestMessage(HttpMethod.Get, "/crud?entity=Product")
        {
            Content = JsonContent.Create(payload, options: jsonOptions)
        };
        var response = await currentClient.SendAsync(message);
        lastStatusCode = response.StatusCode;
        lastResponseBody = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(expected), $"Response payload: {lastResponseBody}");
        if (expected == HttpStatusCode.OK)
        {
            var collection = TryDeserializeCollectionResponse(lastResponseBody);
            lastProducts = collection?.Data;
        }
        else
        {
            lastProducts = null;
        }
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

    [Then(@"the last response reports unknown Product fields (.+)")]
    public void ThenTheLastResponseReportsUnknownProductFields(string fields)
    {
        Assert.That(lastResponseBody, Is.Not.Null, "No response payload was captured.");
        using var document = JsonDocument.Parse(lastResponseBody!);
        var root = document.RootElement;
        Assert.That(root.TryGetProperty("message", out var messageElement), Is.True);
        Assert.That(messageElement.GetString(), Is.EqualTo("Unknown fields in payload"));
        Assert.That(root.TryGetProperty("entity", out var entityElement), Is.True);
        Assert.That(entityElement.GetString(), Is.EqualTo("Product"));
        Assert.That(root.TryGetProperty("fields", out var fieldsElement), Is.True);
        var actual = fieldsElement.EnumerateArray()
            .Select(item => item.GetString())
            .Where(value => value != null)
            .Select(value => value!)
            .ToList();
        var expected = fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Then(@"the last response message is ""(.+)""")]
    public void ThenTheLastResponseMessageIs(string message)
    {
        Assert.That(lastResponseBody, Is.Not.Null, "No response payload was captured.");
        using var document = JsonDocument.Parse(lastResponseBody!);
        Assert.That(document.RootElement.TryGetProperty("message", out var messageElement), Is.True, "Response did not include 'message'.");
        Assert.That(messageElement.GetString(), Is.EqualTo(message));
    }

    [Then(@"the last response masks the following Product fields with ""(.+)""")]
    public void ThenTheLastResponseMasksTheFollowingProductFieldsWith(string mask, Table table)
    {
        Assert.That(lastResponseBody, Is.Not.Null, "No response payload was captured.");
        using var document = JsonDocument.Parse(lastResponseBody!);
        var data = document.RootElement.GetProperty("data");
        foreach (var row in table.Rows)
        {
            var element = ResolveProductElement(data, row["Name"]);
            foreach (var header in table.Header)
            {
                if (string.Equals(header, "Name", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var field = ToCamelCase(header);
                Assert.That(element.TryGetProperty(field, out var property), Is.True, $"Field '{field}' was not present for '{row["Name"]}'.");
                Assert.That(property.ValueKind, Is.EqualTo(JsonValueKind.String), $"Field '{field}' was not suppressed.");
                Assert.That(property.GetString(), Is.EqualTo(mask), $"Field '{field}' was not masked with '{mask}'.");
            }
        }
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
            lastResponseBody = await response.Content.ReadAsStringAsync();
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

    [Then("the EF Core store marks the following products as deleted")]
    public void ThenTheEfCoreStoreMarksTheFollowingProductsAsDeleted(Table table)
    {
        using var scope = server!.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FakeDbContext>();
        foreach (var row in table.Rows)
        {
            var name = row["Name"];
            var record = dbContext.Products.AsNoTracking().SingleOrDefault(product => string.Equals(product.Name, name, StringComparison.OrdinalIgnoreCase));
            Assert.That(record, Is.Not.Null, $"Product '{name}' was not found in the EF Core store.");
            Assert.That(record!.Deleted, Is.True, $"Product '{name}' was not marked as deleted.");
        }
    }

    [Then("the EF Core store sets DeletedAt in UTC for the following products")]
    public void ThenTheEfCoreStoreSetsDeletedAtInUtcForTheFollowingProducts(Table table)
    {
        using var scope = server!.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FakeDbContext>();
        foreach (var row in table.Rows)
        {
            var name = row["Name"];
            var record = dbContext.Products.AsNoTracking().SingleOrDefault(product => string.Equals(product.Name, name, StringComparison.OrdinalIgnoreCase));
            Assert.That(record, Is.Not.Null, $"Product '{name}' was not found in the EF Core store.");
            Assert.That(record!.DeletedAt, Is.Not.Null, $"Product '{name}' does not have DeletedAt set.");
            Assert.That(record.DeletedAt!.Value.Kind, Is.EqualTo(DateTimeKind.Utc), $"Product '{name}' DeletedAt is not UTC.");
        }
    }

    [Then("the EF Core store sets DeletedAt in local time for the following products")]
    public void ThenTheEfCoreStoreSetsDeletedAtInLocalTimeForTheFollowingProducts(Table table)
    {
        using var scope = server!.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FakeDbContext>();
        foreach (var row in table.Rows)
        {
            var name = row["Name"];
            var record = dbContext.Products.AsNoTracking().SingleOrDefault(product => string.Equals(product.Name, name, StringComparison.OrdinalIgnoreCase));
            Assert.That(record, Is.Not.Null, $"Product '{name}' was not found in the EF Core store.");
            Assert.That(record!.DeletedAt, Is.Not.Null, $"Product '{name}' does not have DeletedAt set.");
            Assert.That(record.DeletedAt!.Value.Kind, Is.EqualTo(DateTimeKind.Local), $"Product '{name}' DeletedAt is not local time.");
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

    private static Dictionary<string, object?> BuildRawInput(Table table)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in table.Rows)
        {
            var field = row["field"];
            payload[field] = ParseRawValue(row["value"]);
        }

        return payload;
    }

    private static object? ParseRawValue(string value)
    {
        if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return ParseFilterValue(value);
    }

    private static bool FilterContainsNegativePrice(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("field", out var fieldElement) &&
                    fieldElement.ValueKind == JsonValueKind.String &&
                    string.Equals(fieldElement.GetString(), "price", StringComparison.OrdinalIgnoreCase) &&
                    element.TryGetProperty("value", out var valueElement))
                {
                    if (valueElement.ValueKind == JsonValueKind.Number)
                    {
                        if (valueElement.TryGetDecimal(out var decimalValue) && decimalValue < 0)
                        {
                            return true;
                        }

                        try
                        {
                            if (valueElement.GetDouble() < 0)
                            {
                                return true;
                            }
                        }
                        catch (FormatException)
                        {
                            // ignore
                        }
                        catch (InvalidOperationException)
                        {
                            // ignore
                        }
                    }

                    if (valueElement.ValueKind == JsonValueKind.String &&
                        decimal.TryParse(valueElement.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) &&
                        parsed < 0)
                    {
                        return true;
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Object || property.Value.ValueKind == JsonValueKind.Array)
                    {
                        if (FilterContainsNegativePrice(property.Value))
                        {
                            return true;
                        }
                    }
                }

                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (FilterContainsNegativePrice(item))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
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

    private static object? BuildFilterPayload(Table table)
    {
        if (table.Rows.Count == 0)
        {
            return null;
        }

        object BuildClause(dynamic row)
        {
            var field = (string)row["field"];
            var op = (string)row["op"];
            var value = ParseFilterValue(row["value"]);
            return new FilterClause(field, op, value);
        }

        if (table.Rows.Count == 1)
        {
            return BuildClause(table.Rows[0]);
        }

        var clauses = table.Rows.Select(row => BuildClause(row)).ToArray();
        return new { and = clauses };
    }

    private static object ParseFilterValue(string value)
    {
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
        {
            return decimalValue;
        }

        if (bool.TryParse(value, out var boolValue))
        {
            return boolValue;
        }

        return value;
    }

    private CollectionResponse? TryDeserializeCollectionResponse(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<CollectionResponse>(payload, jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement ResolveProductElement(JsonElement data, string name)
    {
        if (data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("name", out var candidate) && string.Equals(candidate.GetString(), name, StringComparison.OrdinalIgnoreCase))
            {
                return data;
            }
        }
        else if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in data.EnumerateArray())
            {
                if (element.TryGetProperty("name", out var candidate) && string.Equals(candidate.GetString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return element;
                }
            }
        }

        throw new AssertionException($"Product '{name}' was not found in the last response.");
    }

    private static string ToCamelCase(string value)
    {
        if (string.IsNullOrEmpty(value) || char.IsLower(value[0]))
        {
            return value;
        }

        return char.ToLowerInvariant(value[0]) + value[1..];
    }

    private sealed class ProjectionRestrictedProductPolicy : CrudPolicy<Product>
    {
        private ProjectionRestrictedProductPolicy(string? suppression, bool restrictCreate)
        {
            if (!string.IsNullOrWhiteSpace(suppression))
            {
                UseSupression(suppression);
            }

            Configure(restrictCreate);
        }

        public static ProjectionRestrictedProductPolicy ForSupport(string? suppression, bool restrictCreate)
        {
            return new ProjectionRestrictedProductPolicy(suppression, restrictCreate);
        }

        private void Configure(bool restrictCreate)
        {
            var restrictedFields = new Expression<Func<Product, object>>[] { product => product.Id, product => product.Name };
            Allow(CrudAction.Read).ForRoles("Support").ForFields(restrictedFields);
            Allow(CrudAction.Read).ForRoles("Admin");

            if (restrictCreate)
            {
                Allow(CrudAction.Create).ForRoles("Support").ForFields(restrictedFields);
            }
            else
            {
                Allow(CrudAction.Create).ForRoles("Support");
            }

            Allow(CrudAction.Create).ForRoles("Admin");
        }
    }

    private sealed class SoftDeleteProductPolicy : CrudPolicy<Product>
    {
        private SoftDeleteProductPolicy(string role, Expression<Func<Product, bool>> flagSelector, Expression<Func<Product, DateTime?>>? timestampSelector, bool? useUtc)
        {
            Allow(CrudAction.Create).ForRoles(role);
            Allow(CrudAction.Read).ForRoles(role);
            if (timestampSelector == null)
            {
                Allow(CrudAction.Delete).ForRoles(role).DeleteWithColumn(flagSelector);
                return;
            }

            var utc = useUtc ?? true;
            Allow(CrudAction.Delete).ForRoles(role).DeleteWithColumn(flagSelector, timestampSelector, utc);
        }

        public static SoftDeleteProductPolicy ForFlagOnly(string flagField, string role)
        {
            var flagSelector = ResolveFlagSelector(flagField);
            return new SoftDeleteProductPolicy(role, flagSelector, null, null);
        }

        public static SoftDeleteProductPolicy ForTimestamp(string flagField, string timestampField, bool useUtc, string role)
        {
            var flagSelector = ResolveFlagSelector(flagField);
            var timestampSelector = ResolveTimestampSelector(timestampField);
            return new SoftDeleteProductPolicy(role, flagSelector, timestampSelector, useUtc);
        }

        private static Expression<Func<Product, bool>> ResolveFlagSelector(string field)
        {
            if (string.Equals(field, nameof(Product.Deleted), StringComparison.OrdinalIgnoreCase))
            {
                return product => product.Deleted;
            }

            throw new ArgumentException($"Unknown soft delete flag '{field}'.", nameof(field));
        }

        private static Expression<Func<Product, DateTime?>> ResolveTimestampSelector(string field)
        {
            if (string.Equals(field, nameof(Product.DeletedAt), StringComparison.OrdinalIgnoreCase))
            {
                return product => product.DeletedAt;
            }

            throw new ArgumentException($"Unknown soft delete timestamp '{field}'.", nameof(field));
        }
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

    private sealed record RawCreatePayload(string Entity, Dictionary<string, object?> Input, string[] Returning);

    private sealed record UpdateChanges(string Description, decimal Price);

    private sealed record ConditionPayload(string Field, string Op, object Value);

    private sealed record UpdatePayload(string Entity, ConditionPayload Condition, UpdateChanges Update);

    private sealed record UpdateResponse(int AffectedRows);

    private sealed record ReadPayload(string Entity, object? Filter, string[] Select);

    private sealed record FilterClause(string Field, string Op, object Value);

}
