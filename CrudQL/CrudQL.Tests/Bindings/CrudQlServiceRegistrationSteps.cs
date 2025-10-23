using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using NUnit.Framework;

namespace CrudQL.Tests.Bindings;

[Binding]
public class CrudQlServiceRegistrationSteps
{
    private readonly ScenarioContext scenarioContext;
    private IServiceCollection? services;
    private object? builder;
    private Type? builderType;

    public CrudQlServiceRegistrationSteps(ScenarioContext scenarioContext)
    {
        this.scenarioContext = scenarioContext;
    }

    [Given("an empty service collection")]
    public void GivenAnEmptyServiceCollection()
    {
        services = new ServiceCollection();
        scenarioContext["services"] = services;
    }

    [When("I configure CrudQL via AddCrudQl")]
    public void WhenIConfigureCrudQlViaAddCrudQl()
    {
        Assert.That(services, Is.Not.Null, "Services must be initialised");
        var assembly = Assembly.Load("CrudQL.Service");
        var extensionType = assembly.GetType("CrudQL.Service.DependencyInjection.CrudQlServiceCollectionExtensions");
        Assert.That(extensionType, Is.Not.Null, "Extension class CrudQlServiceCollectionExtensions not found");
        var method = extensionType.GetMethod("AddCrudQl", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(IServiceCollection) }, null);
        Assert.That(method, Is.Not.Null, "AddCrudQl extension method not found");
        builder = method.Invoke(null, new object[] { services! });
        builderType = builder?.GetType();
        scenarioContext["builder"] = builder;
        scenarioContext["builderType"] = builderType;
    }

    [Then("the AddCrudQl call should return the CrudQL builder")]
    public void ThenTheAddCrudQlCallShouldReturnTheCrudQlBuilder()
    {
        Assert.That(builder, Is.Not.Null, "AddCrudQl did not return a builder instance");
        var expectedInterface = Assembly.Load("CrudQL.Service").GetType("CrudQL.Service.Configuration.ICrudQlBuilder");
        Assert.That(expectedInterface, Is.Not.Null, "ICrudQlBuilder interface not found");
        Assert.That(expectedInterface!.IsAssignableFrom(builder!.GetType()), "Returned builder does not implement ICrudQlBuilder");
    }

    [Then("the builder should expose the registered services for chaining")]
    public void ThenTheBuilderShouldExposeTheRegisteredServicesForChaining()
    {
        Assert.That(builderType, Is.Not.Null, "Builder type is not captured");
        var servicesProperty = builderType!.GetProperty("Services", BindingFlags.Public | BindingFlags.Instance);
        Assert.That(servicesProperty, Is.Not.Null, "Services property not found on builder");
        var value = servicesProperty!.GetValue(builder);
        Assert.That(value, Is.SameAs(services), "Builder did not return the original service collection");
    }

    [Then("the builder contract should expose entity registration methods")]
    public void ThenTheBuilderContractShouldExposeEntityRegistrationMethods()
    {
        Assert.That(builderType, Is.Not.Null, "Builder type is not captured");
        var expectedInterface = Assembly.Load("CrudQL.Service").GetType("CrudQL.Service.Configuration.ICrudQlBuilder");
        Assert.That(expectedInterface, Is.Not.Null, "ICrudQlBuilder interface not found");
        var addEntity = expectedInterface!.GetMethod("AddEntity", Type.EmptyTypes);
        Assert.That(addEntity, Is.Not.Null, "ICrudQlBuilder.AddEntity method not found");
        Assert.That(addEntity!.IsGenericMethod, "AddEntity should be generic");
        Assert.That(addEntity.ReturnType == expectedInterface, "AddEntity should return ICrudQlBuilder");
        var addEntityWithConfiguration = expectedInterface.GetMethods()
            .SingleOrDefault(method => method.Name == "AddEntity" && method.GetParameters().Length == 1);
        Assert.That(addEntityWithConfiguration, Is.Not.Null, "ICrudQlBuilder.AddEntity overload with configuration not found");
        Assert.That(addEntityWithConfiguration!.IsGenericMethod, "Configurable AddEntity should be generic");
        Assert.That(addEntityWithConfiguration.ReturnType == expectedInterface, "Configurable AddEntity should return ICrudQlBuilder");
        Assert.That(addEntityWithConfiguration.GetParameters()[0].ParameterType.IsGenericType, "AddEntity configuration parameter should be generic");
        var addEntitiesFromDbContext = expectedInterface.GetMethod("AddEntitiesFromDbContext");
        Assert.That(addEntitiesFromDbContext, Is.Not.Null, "ICrudQlBuilder.AddEntitiesFromDbContext method not found");
        Assert.That(addEntitiesFromDbContext!.IsGenericMethod, "AddEntitiesFromDbContext should be generic");
        Assert.That(addEntitiesFromDbContext.ReturnType == expectedInterface, "AddEntitiesFromDbContext should return ICrudQlBuilder");
    }
}
