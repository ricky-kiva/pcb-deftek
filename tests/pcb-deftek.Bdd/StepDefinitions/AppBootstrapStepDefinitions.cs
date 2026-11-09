using System;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using pcb_deftek;
using Reqnroll;

namespace pcb_deftek.Bdd.StepDefinitions;

[Binding]
public class AppBootstrapStepDefinitions : IDisposable
{
    private ServiceCollection? _services;
    private ServiceProvider? _serviceProvider;

    [Given(@"a new service collection for application startup")]
    public void GivenANewServiceCollectionForApplicationStartup()
    {
        _services = new ServiceCollection();
    }

    [When(@"the application dependencies are configured")]
    public void WhenTheApplicationDependenciesAreConfigured()
    {
        _services.Should().NotBeNull();
        App.ConfigureServices(_services!);
        _serviceProvider = _services!.BuildServiceProvider();
    }

    [Then(@"the logging service should be resolvable")]
    public void ThenTheLoggingServiceShouldBeResolvable()
    {
        _serviceProvider.Should().NotBeNull();
        var logger = _serviceProvider!.GetService<ILogger<App>>();
        logger.Should().NotBeNull();
    }

    [Then(@"the MainWindow should be registered as a singleton")]
    public void ThenTheMainWindowShouldBeRegisteredAsASingleton()
    {
        _services.Should().NotBeNull();
        var descriptor = _services!.FirstOrDefault(d => d.ServiceType == typeof(MainWindow));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}
