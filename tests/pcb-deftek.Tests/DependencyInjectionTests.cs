using System;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using pcb_deftek;
using pcb_deftek.Infrastructure.Inference;
using pcb_deftek.Infrastructure.Persistence;
using pcb_deftek.Infrastructure.Vision;
using pcb_deftek.Infrastructure.Persistence.DatabaseBackup;
using pcb_deftek.Infrastructure.Persistence.Migrations;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using Xunit;

namespace pcb_deftek.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void ConfigureServices_WhenCalled_RegistersLoggingInfrastructure()
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
    public void ConfigureServices_WhenCalled_RegistersMainWindowAsSingletonDescriptor()
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

    [Fact]
    public void ConfigureServices_OnStaThread_ResolvesSameMainWindowInstanceMultipleTimes()
    {
        // Arrange
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        MainWindow? firstInstance = null;
        MainWindow? secondInstance = null;

        // Act: WPF controls require Single-Threaded Apartment (STA) thread
        var thread = new Thread(() =>
        {
            firstInstance = provider.GetRequiredService<MainWindow>();
            secondInstance = provider.GetRequiredService<MainWindow>();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        // Assert
        firstInstance.Should().NotBeNull();
        secondInstance.Should().NotBeNull();
        firstInstance.Should().BeSameAs(secondInstance, "Singleton registration must resolve to the identical instance reference");
    }

    [Fact]
    public void Services_WhenAppUninitialized_ThrowsInvalidOperationException()
    {
        // Arrange
        var app = new App();

        // Act
        Action act = () => _ = app.Services;

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Service provider has not been initialized.");
    }

    [Theory]
    [InlineData(typeof(ISqliteConnectionFactory))]
    [InlineData(typeof(IDatabaseMigrator))]
    [InlineData(typeof(IInspectionRepository))]
    [InlineData(typeof(IDefectRepository))]
    [InlineData(typeof(ISettingsRepository))]
    [InlineData(typeof(IDatabaseBackupService))]
    public void ConfigureServices_WhenCalled_RegistersPersistenceServicesAsSingletons(Type serviceType)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        App.ConfigureServices(services);
        var descriptor = services.FirstOrDefault(d => d.ServiceType == serviceType);

        // Assert
        descriptor.Should().NotBeNull($"Service {serviceType.Name} must be registered");
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void ConfigureServices_WhenProviderBuilt_ResolvesAllPersistenceServicesSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        // Act & Assert
        provider.GetRequiredService<ISqliteConnectionFactory>().Should().NotBeNull();
        provider.GetRequiredService<IDatabaseMigrator>().Should().NotBeNull();
        provider.GetRequiredService<IInspectionRepository>().Should().NotBeNull();
        provider.GetRequiredService<IDefectRepository>().Should().NotBeNull();
        provider.GetRequiredService<ISettingsRepository>().Should().NotBeNull();
        provider.GetRequiredService<IDatabaseBackupService>().Should().NotBeNull();
    }

    [Theory]
    [InlineData(typeof(IOnnxModelRunner))]
    [InlineData(typeof(IVisionPreprocessor))]
    [InlineData(typeof(IYoloPostprocessor))]
    [InlineData(typeof(IPcbDefectDetector))]
    public void ConfigureServices_WhenCalled_RegistersInferenceServicesAsSingletons(Type serviceType)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        App.ConfigureServices(services);
        var descriptor = services.FirstOrDefault(d => d.ServiceType == serviceType);

        // Assert
        descriptor.Should().NotBeNull($"Service {serviceType.Name} must be registered");
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void ConfigureServices_WhenProviderBuilt_ResolvesAllInferenceServicesSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        // Act & Assert
        provider.GetRequiredService<IOnnxModelRunner>().Should().NotBeNull();
        provider.GetRequiredService<IVisionPreprocessor>().Should().NotBeNull();
        provider.GetRequiredService<IYoloPostprocessor>().Should().NotBeNull();
        provider.GetRequiredService<IPcbDefectDetector>().Should().NotBeNull();
    }
}
