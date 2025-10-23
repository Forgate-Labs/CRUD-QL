using System;
using System.Linq;
using CrudQL.Service.Entities;
using CrudQL.Tests.TestAssets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Reqnroll;

namespace CrudQL.Tests.Bindings;

[Binding]
public sealed class CrudQlEntityRegistrationSteps : IDisposable
{
    private readonly ScenarioContext scenarioContext;
    private ServiceProvider? serviceProvider;

    public CrudQlEntityRegistrationSteps(ScenarioContext scenarioContext)
    {
        this.scenarioContext = scenarioContext;
    }

    [When("I add the Product entity to CrudQL")]
    public void WhenIAddTheProductEntityToCrudQl()
    {
        var builder = GetBuilder();
        var builderType = GetBuilderType();
        var method = builderType.GetMethod("AddEntity", Type.EmptyTypes);
        Assert.That(method, Is.Not.Null, "AddEntity method was not found on the builder");
        var generic = method!.MakeGenericMethod(typeof(Product));
        var result = generic.Invoke(builder, Array.Empty<object>());
        UpdateBuilder(result ?? builder);
    }

    [Given("a DbContext registered with in-memory storage")]
    public void GivenADbContextRegisteredWithInMemoryStorage()
    {
        var services = GetServices();
        services.AddDbContext<FakeDbContext>(options => options.UseInMemoryDatabase($"CrudQlTests_{Guid.NewGuid()}"));
        ResetProvider();
    }

    [When("I register the entities from the FakeDbContext")]
    public void WhenIRegisterTheEntitiesFromTheFakeDbContext()
    {
        var builder = GetBuilder();
        var builderType = GetBuilderType();
        var method = builderType.GetMethod("AddEntitiesFromDbContext");
        Assert.That(method, Is.Not.Null, "AddEntitiesFromDbContext method was not found on the builder");
        var generic = method!.MakeGenericMethod(typeof(FakeDbContext));
        var result = generic.Invoke(builder, Array.Empty<object>());
        UpdateBuilder(result ?? builder);
    }

    [Then("the entity registry should list Product with its CLR type")]
    public void ThenTheEntityRegistryShouldListProductWithItsClrType()
    {
        using var scope = GetServiceProvider().CreateScope();
        var registry = scope.ServiceProvider.GetService<ICrudEntityRegistry>();
        Assert.That(registry, Is.Not.Null, "ICrudEntityRegistry is not registered");
        var registration = registry!.Entities.SingleOrDefault(entry => entry.ClrType == typeof(Product));
        Assert.That(registration, Is.Not.Null, "Product registration not present in the registry");
        Assert.That(registration!.EntityName, Is.EqualTo("Product"), "Entity name did not match");
    }

    [Then("resolving the Product entity set should yield an EF Core DbSet")]
    public void ThenResolvingTheProductEntitySetShouldYieldAnEfCoreDbSet()
    {
        using var scope = GetServiceProvider().CreateScope();
        var registry = scope.ServiceProvider.GetService<ICrudEntityRegistry>();
        Assert.That(registry, Is.Not.Null, "ICrudEntityRegistry is not registered");
        var dbContext = scope.ServiceProvider.GetService<FakeDbContext>();
        Assert.That(dbContext, Is.Not.Null, "FakeDbContext should be registered via AddEntitiesFromDbContext");
        var set = registry!.ResolveSet<Product>(scope.ServiceProvider);
        Assert.That(set, Is.InstanceOf<DbSet<Product>>(), "Registry did not return an EF Core DbSet");
    }

    public void Dispose()
    {
        serviceProvider?.Dispose();
    }

    private IServiceCollection GetServices()
    {
        Assert.That(scenarioContext.TryGetValue("services", out var servicesObj), Is.True, "Service collection is not initialised");
        return (IServiceCollection)servicesObj!;
    }

    private object GetBuilder()
    {
        Assert.That(scenarioContext.TryGetValue("builder", out var builder), Is.True, "Builder instance is not initialised");
        return builder!;
    }

    private Type GetBuilderType()
    {
        Assert.That(scenarioContext.TryGetValue("builderType", out var builderType), Is.True, "Builder type is not initialised");
        return (Type)builderType!;
    }

    private void UpdateBuilder(object builder)
    {
        scenarioContext["builder"] = builder;
        scenarioContext["builderType"] = builder.GetType();
        ResetProvider();
    }

    private ServiceProvider GetServiceProvider()
    {
        serviceProvider ??= GetServices().BuildServiceProvider();
        return serviceProvider;
    }

    private void ResetProvider()
    {
        serviceProvider?.Dispose();
        serviceProvider = null;
    }
}
