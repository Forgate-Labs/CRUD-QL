using System;
using CrudQL.Service.Authorization;
using CrudQL.Service.DependencyInjection;
using CrudQL.Tests.TestAssets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Reqnroll;

namespace CrudQL.Tests.Bindings;

[Binding]
public class CrudQlDependencyInjectionSteps
{
    private IServiceCollection? services;
    private ServiceProvider? provider;
    private Exception? buildException;

    [Given("an empty service collection with scope validation enabled")]
    public void GivenAnEmptyServiceCollectionWithScopeValidationEnabled()
    {
        services = new ServiceCollection();
        services.AddLogging();
    }

    [When("I register CrudQL with a DbContext and an entity")]
    public void WhenIRegisterCrudQlWithADbContextAndAnEntity()
    {
        Assert.That(services, Is.Not.Null);

        services!.AddDbContext<FakeDbContext>(options =>
            options.UseInMemoryDatabase($"DIValidation_{Guid.NewGuid()}"));

        services.AddCrudQl()
            .AddEntity<Product>(cfg =>
            {
                cfg.UsePolicy(new OpenProductPolicy());
            })
            .AddEntitiesFromDbContext<FakeDbContext>();

        try
        {
            provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
        }
        catch (Exception ex)
        {
            buildException = ex;
        }
    }

    [Then("building the service provider should not throw")]
    public void ThenBuildingTheServiceProviderShouldNotThrow()
    {
        Assert.That(buildException, Is.Null,
            $"BuildServiceProvider threw: {buildException?.Message}");
        Assert.That(provider, Is.Not.Null);
    }

    [Then("the IModelCustomizer should be resolvable from a scope")]
    public void ThenTheIModelCustomizerShouldBeResolvableFromAScope()
    {
        Assert.That(provider, Is.Not.Null);

        using var scope = provider!.CreateScope();
        var customizer = scope.ServiceProvider.GetService<IModelCustomizer>();
        Assert.That(customizer, Is.Not.Null, "IModelCustomizer should be resolvable from a scope");
    }

    private sealed class OpenProductPolicy : CrudPolicy<Product>
    {
        public OpenProductPolicy()
        {
            AllowAnonymousRead().ForAllFields();
            AllowAnonymousCreate().ForAllFields();
            AllowAnonymousUpdate();
            AllowAnonymousDelete();
        }
    }
}
