using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Infrastructure.Persistence.Migrations;
using pcb_deftek.Infrastructure.Startup;
using pcb_deftek.Presentation.Navigation;
using Xunit;

namespace pcb_deftek.Tests;

public class StartupCoordinatorTests
{
    [Fact]
    public async Task RunAsync_WhenMigrationFails_ReturnsFailureAndDoesNotRoute()
    {
        // Arrange
        var migrator = new RecordingMigrator(failure: new DatabaseMigrationException("database is corrupt"));
        var router = new RecordingRouter();
        using ServiceProvider provider = BuildProvider(migrator, router);

        var sut = new StartupCoordinator(NullLogger<StartupCoordinator>.Instance);

        // Act
        StartupResult result = await sut.RunAsync(provider);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Failure.Should().BeSameAs(migrator.Failure);
        migrator.Calls.Should().Be(1);
        router.Calls.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_WhenRoutingFails_ReturnsSuccess()
    {
        // Arrange
        var migrator = new RecordingMigrator();
        var router = new RecordingRouter(failure: new InvalidOperationException("preference lookup failed"));
        using ServiceProvider provider = BuildProvider(migrator, router);

        var sut = new StartupCoordinator(NullLogger<StartupCoordinator>.Instance);

        // Act
        StartupResult result = await sut.RunAsync(provider);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Failure.Should().BeNull();
        migrator.Calls.Should().Be(1);
        router.Calls.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_WhenBothSucceed_RunsMigrationThenRouting()
    {
        // Arrange
        var steps = new List<string>();
        var migrator = new RecordingMigrator(steps: steps);
        var router = new RecordingRouter(steps: steps);
        using ServiceProvider provider = BuildProvider(migrator, router);

        var sut = new StartupCoordinator(NullLogger<StartupCoordinator>.Instance);

        // Act
        StartupResult result = await sut.RunAsync(provider);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Failure.Should().BeNull();
        steps.Should().Equal("migrate", "route");
    }

    private static ServiceProvider BuildProvider(IDatabaseMigrator migrator, IStartupRoutingService router)
    {
        var services = new ServiceCollection();
        services.AddSingleton(migrator);
        services.AddSingleton(router);
        return services.BuildServiceProvider();
    }

    private sealed class RecordingMigrator : IDatabaseMigrator
    {
        private readonly List<string>? _steps;

        public RecordingMigrator(Exception? failure = null, List<string>? steps = null)
        {
            Failure = failure;
            _steps = steps;
        }

        public Exception? Failure { get; }

        public int Calls { get; private set; }

        public Task<MigrationResult> MigrateToLatestAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            _steps?.Add("migrate");

            if (Failure is not null)
            {
                return Task.FromException<MigrationResult>(Failure);
            }

            return Task.FromResult(new MigrationResult(true, 0, 1, 1, "Applied"));
        }

        public Task<MigrationResult> RollbackAsync(int targetVersion, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> GetCurrentVersionAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MigrationHistoryEntry>> GetMigrationHistoryAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingRouter : IStartupRoutingService
    {
        private readonly List<string>? _steps;
        private readonly Exception? _failure;

        public RecordingRouter(Exception? failure = null, List<string>? steps = null)
        {
            _failure = failure;
            _steps = steps;
        }

        public int Calls { get; private set; }

        public Task RouteStartupAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            _steps?.Add("route");

            return _failure is null
                ? Task.CompletedTask
                : Task.FromException(_failure);
        }
    }
}
