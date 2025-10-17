using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CrudQL.Service.DependencyInjection;
using CrudQL.Service.Routing;
using CrudQL.Tests.TestAssets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Reqnroll;

namespace CrudQL.Tests.Bindings;

[Binding]
public class CrudQlEndpointMappingSteps
{
    private IEndpointRouteBuilder? endpointRouteBuilder;
    private IReadOnlyList<RouteEndpoint>? endpoints;
    private Exception? capturedException;
    private RouteEndpoint? selectedEndpoint;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private string? databaseName;

    [Given("a web application instance")]
    public void GivenAWebApplicationInstance()
    {
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();
        endpointRouteBuilder = app;
        endpoints = null;
        capturedException = null;
        selectedEndpoint = null;
    }

    [Given("a null endpoint route builder")]
    public void GivenANullEndpointRouteBuilder()
    {
        endpointRouteBuilder = null;
        endpoints = null;
        capturedException = null;
        selectedEndpoint = null;
    }

    [When("I map CrudQL with MapCrudQl")]
    public void WhenIMapCrudQlWithMapCrudQl()
    {
        Assert.That(endpointRouteBuilder, Is.Not.Null, "Endpoint route builder must be initialised");
        capturedException = null;
        selectedEndpoint = null;
        endpointRouteBuilder!.MapCrudQl();
        endpoints = endpointRouteBuilder.DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }

    [When("I invoke MapCrudQl")]
    public void WhenIInvokeMapCrudQl()
    {
        capturedException = Assert.Throws<ArgumentNullException>(() =>
        {
            endpointRouteBuilder!.MapCrudQl();
        });
    }

    [Then(@"the endpoint set should contain \/crud for (.*)")]
    public void ThenTheEndpointSetShouldContainCrudForVerb(string verb)
    {
        Assert.That(endpoints, Is.Not.Null, "Endpoints should be captured");
        var matches = endpoints!.Where(endpoint =>
        {
            if (!string.Equals(endpoint.RoutePattern.RawText, "/crud", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            var methodMetadata = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
            return methodMetadata?.HttpMethods.Any(httpMethod => string.Equals(httpMethod, verb, StringComparison.OrdinalIgnoreCase)) == true;
        });
        Assert.That(matches, Is.Not.Empty, $"No endpoint matched /crud for {verb}");
        selectedEndpoint = matches.First();
    }

    [Then("an argument null exception should be thrown")]
    public void ThenAnArgumentNullExceptionShouldBeThrown()
    {
        Assert.That(capturedException, Is.TypeOf<ArgumentNullException>(), "Expected ArgumentNullException to be thrown");
    }

    [Then(@"calling the \/crud endpoint for (.*?)(?: with the documented payload)? should return ok")]
    public async Task ThenCallingTheCrudEndpointForVerbShouldReturnOk(string verb)
    {
        verb = verb.Trim();
        Assert.That(selectedEndpoint, Is.Not.Null, "Endpoint must be selected before invoking");
        var requestDelegate = selectedEndpoint!.RequestDelegate;
        Assert.That(requestDelegate, Is.Not.Null, "Endpoint does not expose a request delegate");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        databaseName = $"CrudEndpoint_{Guid.NewGuid()}";
        services.AddDbContext<FakeDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddCrudQl()
            .AddEntity<Product>()
            .AddEntitiesFromDbContext<FakeDbContext>();

        using var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = verb.ToUpperInvariant();
        httpContext.Request.Path = "/crud";
        httpContext.Response.Body = new MemoryStream();
        using var requestScope = serviceProvider.CreateScope();
        httpContext.RequestServices = requestScope.ServiceProvider;

        int? seededId = null;
        if (RequiresExistingEntity(httpContext.Request.Method))
        {
            seededId = await SeedEntityAsync(serviceProvider);
        }

        var payload = CreatePayload(httpContext.Request.Method, seededId);
        if (payload != null)
        {
            var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);
            httpContext.Request.Body = new MemoryStream(payloadBytes, writable: false);
            httpContext.Request.ContentLength = payloadBytes.Length;
            httpContext.Request.ContentType = "application/json";
        }
        else
        {
            httpContext.Request.Body = new MemoryStream();
        }

        await requestDelegate!(httpContext);

        var expectedStatus = httpContext.Request.Method switch
        {
            var method when string.Equals(method, HttpMethods.Post, StringComparison.OrdinalIgnoreCase) => StatusCodes.Status201Created,
            var method when string.Equals(method, HttpMethods.Put, StringComparison.OrdinalIgnoreCase) => StatusCodes.Status200OK,
            var method when string.Equals(method, HttpMethods.Delete, StringComparison.OrdinalIgnoreCase) => StatusCodes.Status204NoContent,
            _ => StatusCodes.Status200OK
        };

        Assert.That(httpContext.Response.StatusCode, Is.EqualTo(expectedStatus), "Unexpected status code from /crud endpoint");

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(httpContext.Response.Body, leaveOpen: true);
        var body = reader.ReadToEnd();

        if (expectedStatus == StatusCodes.Status204NoContent)
        {
            Assert.That(body, Is.Empty);
        }
        else
        {
            Assert.That(body, Does.Contain("\"data\""), "Response payload should expose a data envelope");
        }
    }

    private static bool RequiresExistingEntity(string method)
    {
        return string.Equals(method, HttpMethods.Put, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(method, HttpMethods.Delete, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<int> SeedEntityAsync(ServiceProvider services)
    {
        Assert.That(endpoints, Is.Not.Null, "Endpoints must be captured before seeding");
        var postEndpoint = endpoints!.FirstOrDefault(endpoint =>
        {
            var metadata = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
            return metadata?.HttpMethods.Any(method => string.Equals(method, HttpMethods.Post, StringComparison.OrdinalIgnoreCase)) == true;
        });

        Assert.That(postEndpoint, Is.Not.Null, "POST endpoint not found for seeding");
        var requestDelegate = postEndpoint!.RequestDelegate;
        Assert.That(requestDelegate, Is.Not.Null, "POST endpoint has no request delegate");

        var seedContext = new DefaultHttpContext();
        seedContext.Request.Method = HttpMethods.Post;
        seedContext.Request.Path = "/crud";
        seedContext.Response.Body = new MemoryStream();
        using var requestScope = services.CreateScope();
        seedContext.RequestServices = requestScope.ServiceProvider;

        var seedPayload = new
        {
            entity = "Product",
            input = new { name = "Seed Product", price = 99.9M, description = "Seed", currency = "USD" },
            returning = new[] { "id" }
        };

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(seedPayload, SerializerOptions);
        seedContext.Request.Body = new MemoryStream(payloadBytes, writable: false);
        seedContext.Request.ContentLength = payloadBytes.Length;
        seedContext.Request.ContentType = "application/json";

        await requestDelegate!(seedContext);
        Assert.That(seedContext.Response.StatusCode, Is.EqualTo(StatusCodes.Status201Created), "Seeding POST request failed");

        seedContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(seedContext.Response.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        seedContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").GetProperty("id").GetInt32();
    }

    private static object? CreatePayload(string method, int? entityId)
    {
        if (string.Equals(method, HttpMethods.Post, StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                entity = "Product",
                input = new { name = "Mouse Pro", price = 129.9M, description = "Wireless", currency = "USD" },
                returning = new[] { "id", "name", "price", "currency" }
            };
        }

        if (string.Equals(method, HttpMethods.Get, StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                entity = "Product",
                select = new object[] { "id", "name", "price" }
            };
        }

        if (string.Equals(method, HttpMethods.Put, StringComparison.OrdinalIgnoreCase))
        {
            Assert.That(entityId, Is.Not.Null, "PUT payload requires a seeded entity id");
            return new
            {
                entity = "Product",
                key = new { id = entityId.Value },
                input = new { price = 149.9M, description = "Updated" },
                update = new[] { "id", "price", "description" }
            };
        }

        if (string.Equals(method, HttpMethods.Delete, StringComparison.OrdinalIgnoreCase))
        {
            Assert.That(entityId, Is.Not.Null, "DELETE payload requires a seeded entity id");
            return new
            {
                entity = "Product",
                key = new { id = entityId.Value }
            };
        }

        return null;
    }
}
