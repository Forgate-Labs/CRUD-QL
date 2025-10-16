using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CrudQL.Service.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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

    [Then("the endpoint set should contain /crud for (.*)")]
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

    [Then("calling the /crud endpoint for (.*) should return ok")]
    public async Task ThenCallingTheCrudEndpointForVerbShouldReturnOk(string verb)
    {
        Assert.That(selectedEndpoint, Is.Not.Null, "Endpoint must be selected before invoking");
        var requestDelegate = selectedEndpoint!.RequestDelegate;
        Assert.That(requestDelegate, Is.Not.Null, "Endpoint does not expose a request delegate");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = verb.ToUpperInvariant();
        httpContext.Request.Path = "/crud";
        httpContext.Response.Body = new MemoryStream();
        httpContext.RequestServices = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        await requestDelegate!(httpContext);

        Assert.That(httpContext.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK), "Expected HTTP 200 response");
        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(httpContext.Response.Body, leaveOpen: true);
        var body = reader.ReadToEnd();
        Assert.That(body, Does.Contain("\"message\":\"ok\""), "Response body did not contain the expected payload");
    }
}
