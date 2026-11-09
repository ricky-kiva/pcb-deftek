using System;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using pcb_deftek;
using Xunit;

namespace pcb_deftek.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void ConfigureServices_ShouldRegisterLoggingServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        App.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var logger = provider.GetService<ILogger<App>>();

        // Assert
        logger.Should().NotBeNull();
    }

    [Fact]
    public void ConfigureServices_ShouldRegisterMainWindowAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        App.ConfigureServices(services);
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(MainWindow));

        // Assert
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }
}
