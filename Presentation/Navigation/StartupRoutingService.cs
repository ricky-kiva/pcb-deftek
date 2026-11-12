using System;
using System.Threading;
using System.Threading.Tasks;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Presentation.ViewModels;
using Serilog;

namespace pcb_deftek.Presentation.Navigation;

/// <summary>
/// Service responsible for querying user preferences from <see cref="ISettingsRepository"/>
/// and activating the initial application page view model upon startup.
/// </summary>
public class StartupRoutingService : IStartupRoutingService
{
    public const string SettingKeySkipDashboardOnStartup = "Settings:App:SkipDashboardOnStartup";
    public const string SettingKeyDefaultStartupPage = "Settings:App:DefaultStartupPage";

    private readonly ISettingsRepository _settingsRepository;
    private readonly INavigationService _navigationService;

    /// <summary>
    /// Initializes a new instance of <see cref="StartupRoutingService"/>.
    /// </summary>
    public StartupRoutingService(ISettingsRepository settingsRepository, INavigationService navigationService)
    {
        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    }

    /// <inheritdoc />
    public async Task RouteStartupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Check whether the user has chosen to bypass the dashboard on startup
            bool skipDashboard = await _settingsRepository.GetSettingAsync<bool>(
                SettingKeySkipDashboardOnStartup, cancellationToken).ConfigureAwait(false);

            if (skipDashboard)
            {
                Log.Information("Startup routing: 'Skip dashboard on startup' preference active. Navigating directly to Live Detection.");
                await _navigationService.NavigateToAsync<LiveDetectionViewModel>().ConfigureAwait(false);
                return;
            }

            // 2. Query configured default startup page
            string? configuredPage = await _settingsRepository.GetSettingAsync<string>(
                SettingKeyDefaultStartupPage, cancellationToken).ConfigureAwait(false);

            string pageKey = configuredPage?.Trim().ToLowerInvariant() ?? string.Empty;

            switch (pageKey)
            {
                case "live" or "livedetection" or "live detection":
                    Log.Information("Startup routing: Default page configured to Live Detection.");
                    await _navigationService.NavigateToAsync<LiveDetectionViewModel>().ConfigureAwait(false);
                    break;

                case "file" or "filedetection" or "file-based detection":
                    Log.Information("Startup routing: Default page configured to File-Based Detection.");
                    await _navigationService.NavigateToAsync<FileDetectionViewModel>().ConfigureAwait(false);
                    break;

                case "history":
                    Log.Information("Startup routing: Default page configured to History.");
                    await _navigationService.NavigateToAsync<HistoryViewModel>().ConfigureAwait(false);
                    break;

                default:
                    Log.Information("Startup routing: Default page is Dashboard (default).");
                    await _navigationService.NavigateToAsync<DashboardViewModel>().ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Failed to evaluate startup routing preferences; defaulting to Dashboard: {Message}", ex.Message);
            await _navigationService.NavigateToAsync<DashboardViewModel>().ConfigureAwait(false);
        }
    }
}
