using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using pcb_deftek.Infrastructure.Persistence.Migrations;
using pcb_deftek.Presentation.Navigation;

namespace pcb_deftek.Infrastructure.Startup;

/// <summary>
/// Outcome of the application startup sequence.
/// </summary>
/// <param name="Succeeded">Whether the application may continue to the main window.</param>
/// <param name="Failure">The blocking failure when <paramref name="Succeeded"/> is false; otherwise null.</param>
internal sealed record StartupResult(bool Succeeded, Exception? Failure)
{
    /// <summary>
    /// Creates a successful startup result.
    /// </summary>
    public static StartupResult Success() => new(true, null);

    /// <summary>
    /// Creates a failed startup result carrying the blocking exception.
    /// </summary>
    /// <param name="failure">The exception that prevents the application from continuing.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="failure"/> is null.</exception>
    public static StartupResult Failed(Exception failure) =>
        new(false, failure ?? throw new ArgumentNullException(nameof(failure)));
}

/// <summary>
/// Runs the pre-window startup sequence: database migrations (fatal) then startup routing (non-fatal).
/// </summary>
internal sealed class StartupCoordinator
{
    private readonly ILogger<StartupCoordinator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StartupCoordinator"/> class.
    /// </summary>
    /// <param name="logger">Logger for non-fatal startup warnings.</param>
    public StartupCoordinator(ILogger<StartupCoordinator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Applies pending migrations and evaluates startup routing.
    /// </summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="cancellationToken">A token to cancel the startup sequence.</param>
    /// <returns>
    /// A failed result when migrations throw; otherwise a successful result. Routing failures are
    /// logged and do not block startup.
    /// </returns>
    public async Task<StartupResult> RunAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var migrator = services.GetRequiredService<IDatabaseMigrator>();
        try
        {
            await migrator.MigrateToLatestAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return StartupResult.Failed(ex);
        }

        try
        {
            var router = services.GetRequiredService<IStartupRoutingService>();
            await router.RouteStartupAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Non-critical startup routing evaluation warning: {Message}",
                ex.Message);
        }

        return StartupResult.Success();
    }
}
