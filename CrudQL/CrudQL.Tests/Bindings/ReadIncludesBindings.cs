using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
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
public sealed class ReadIncludesBindings : IDisposable
{
    private readonly JsonSerializerOptions serializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private TestServer? server;
    private HttpClient? client;
    private HttpResponseMessage? lastResponse;
    private JsonDocument? lastDocument;
    private ClaimsPrincipal currentUser = new(new ClaimsIdentity());

    [Given(@"a configured CrudQL service with entity ""(.+)"" allowing include ""(.+)"" for role ""(.+)""")]
    public void GivenConfiguredCrudQlService(string entity, string includePath, string role)
    {
        DisposeServer();

        if (!string.Equals(entity, nameof(Product), StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail($"Unsupported entity '{entity}' for include tests.");
        }

        var hostBuilder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddRouting();
                var databaseName = $"CrudQlInclude_{Guid.NewGuid():N}";
                services.AddDbContext<FakeDbContext>(options => options.UseInMemoryDatabase(databaseName));
                services.AddCrudQl()
                    .AddEntity<Product>(cfg =>
                    {
                        cfg.AllowInclude(includePath, role);
                    })
                    .AddEntitiesFromDbContext<FakeDbContext>();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.Use(async (context, next) =>
                {
                    context.User = currentUser;
                    await next();
                });
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapCrudQl();
                });
            });

        server = new TestServer(hostBuilder);
        client = server.CreateClient();
        using var scope = server.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FakeDbContext>();
        dbContext.Database.EnsureCreated();
    }

    [Given(@"an authenticated user with role ""(.+)""")]
    public void GivenAuthenticatedUserWithRole(string role)
    {
        var roles = ParseRoles(role);
        SetUserRoles(roles);
    }

    [Given(@"an authenticated user without role ""(.+)""")]
    public void GivenAuthenticatedUserWithoutRole(string role)
    {
        SetUserRoles(Array.Empty<string>());
    }

    [Given(@"the data store contains a product ""(.+)"" linked to category ""(.+)""")]
    public async Task GivenDataStoreContainsProductLinkedToCategory(string productName, string categoryTitle)
    {
        Assert.That(server, Is.Not.Null, "CrudQL service is not configured.");
        await using var scope = server!.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FakeDbContext>();
        var category = new Category
        {
            Id = 1,
            Title = categoryTitle
        };
        dbContext.Categories.Add(category);
        dbContext.Products.Add(new Product
        {
            Id = 1,
            Name = productName,
            Description = productName,
            Price = 100,
            Currency = "USD",
            CategoryId = category.Id,
            Category = category
        });
        await dbContext.SaveChangesAsync();
    }

    [When(@"the client sends a read request for ""(.+)"" selecting (.+)")]
    public async Task WhenClientSendsReadRequest(string entity, string selectJson)
    {
        var currentClient = EnsureClient();
        var encodedSelect = Uri.EscapeDataString(selectJson);
        var url = $"/crud?entity={entity}&select={encodedSelect}";
        lastResponse?.Dispose();
        lastResponse = await currentClient.GetAsync(url);
        lastDocument?.Dispose();
        if (lastResponse.Content.Headers.ContentLength is { } length && length == 0)
        {
            lastDocument = null;
            return;
        }

        await using var stream = await lastResponse.Content.ReadAsStreamAsync();
        if (stream.CanSeek && stream.Length == 0)
        {
            lastDocument = null;
            return;
        }

        lastDocument = await JsonDocument.ParseAsync(stream);
    }

    [Then(@"the response status is (.+)")]
    public void ThenResponseStatusIs(string status)
    {
        Assert.That(lastResponse, Is.Not.Null, "No response captured.");
        var expected = ParseStatus(status);
        Assert.That(lastResponse!.StatusCode, Is.EqualTo(expected));
    }

    [Then(@"the payload contains the product ""(.+)"" with category title ""(.+)""")]
    public void ThenPayloadContainsProductWithCategory(string productName, string categoryTitle)
    {
        Assert.That(lastDocument, Is.Not.Null, "No JSON payload captured.");
        var root = lastDocument!.RootElement;
        Assert.That(root.TryGetProperty("data", out var data), "Response does not contain a data element.");
        Assert.That(data.ValueKind, Is.EqualTo(JsonValueKind.Array), "Response data is not an array.");
        var match = data.EnumerateArray().FirstOrDefault(item =>
        {
            if (!item.TryGetProperty("name", out var nameElement))
            {
                return false;
            }

            return string.Equals(nameElement.GetString(), productName, StringComparison.OrdinalIgnoreCase);
        });
        Assert.That(match.ValueKind, Is.Not.EqualTo(JsonValueKind.Undefined), $"No product named '{productName}' was returned.");
        Assert.That(match.TryGetProperty("category", out var categoryElement), "Returned product did not include the category object.");
        Assert.That(categoryElement.ValueKind, Is.EqualTo(JsonValueKind.Object), "Category element is not an object.");
        Assert.That(categoryElement.TryGetProperty("title", out var titleElement), "Category object is missing the title field.");
        Assert.That(titleElement.GetString(), Is.EqualTo(categoryTitle));
    }

    [Then(@"the payload message states that include ""(.+)"" is not allowed for the current user")]
    public void ThenPayloadMessageStatesIncludeIsNotAllowed(string includePath)
    {
        Assert.That(lastDocument, Is.Not.Null, "No JSON payload captured.");
        var root = lastDocument!.RootElement;
        Assert.That(root.TryGetProperty("message", out var messageElement), "Response did not include a message element.");
        var message = messageElement.GetString();
        Assert.That(message, Is.EqualTo($"Include '{includePath}' is not allowed for the current user"));
    }

    public void Dispose()
    {
        DisposeServer();
    }

    private void SetUserRoles(IEnumerable<string> roles)
    {
        var roleArray = roles.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToArray();
        if (roleArray.Length == 0)
        {
            currentUser = new ClaimsPrincipal(new ClaimsIdentity());
            return;
        }

        var identity = new ClaimsIdentity("TestAuth");
        foreach (var role in roleArray)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        currentUser = new ClaimsPrincipal(identity);
    }

    private static string[] ParseRoles(string roles)
    {
        return roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private HttpClient EnsureClient()
    {
        Assert.That(client, Is.Not.Null, "CrudQL client is not configured.");
        return client!;
    }

    private static HttpStatusCode ParseStatus(string value)
    {
        if (int.TryParse(value, out var numeric))
        {
            return (HttpStatusCode)numeric;
        }

        return Enum.Parse<HttpStatusCode>(value, true);
    }

    private void DisposeServer()
    {
        lastDocument?.Dispose();
        lastDocument = null;
        lastResponse?.Dispose();
        lastResponse = null;
        client?.Dispose();
        client = null;
        server?.Dispose();
        server = null;
        currentUser = new ClaimsPrincipal(new ClaimsIdentity());
    }
}
