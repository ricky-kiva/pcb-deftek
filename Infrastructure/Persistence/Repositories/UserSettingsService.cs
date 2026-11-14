using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using pcb_deftek.Common.Storage;
using pcb_deftek.Domain.Models;
using pcb_deftek.Domain.Models.Messages;
using pcb_deftek.Infrastructure.Persistence.DatabaseExport;
using Serilog;

namespace pcb_deftek.Infrastructure.Persistence.Repositories;

/// <summary>
/// Service coordinating settings retrieval, persistence into SQLite via <see cref="ISettingsRepository"/>,
/// and reactive broadcasting via <see cref="WeakReferenceMessenger"/>.
/// </summary>
public class UserSettingsService : IUserSettingsService
{
    public const string KeyConfidenceThreshold = "Settings:Inference:ConfidenceThreshold";
    public const string KeyPreferredDevice = "Settings:Inference:PreferredDevice";
    public const string KeyDefaultStartupPage = "Settings:App:DefaultStartupPage";
    public const string KeySkipDashboardOnStartup = "Settings:App:SkipDashboardOnStartup";
    public const string KeyReportExportPath = "Settings:Export:ReportPath";
    public const string KeyDatabaseExportPath = "Settings:Export:DatabasePath";
    public const string KeyImageOutputDirectory = "Settings:Export:ImagePath";
    private const string LegacyCropOutputDirectoryKey = "Settings:Export:CropPath";

    private readonly ISettingsRepository _settingsRepository;
    private readonly IDatabaseExportService _exportService;

    /// <summary>
    /// Initializes a new instance of <see cref="UserSettingsService"/>.
    /// </summary>
    public UserSettingsService(ISettingsRepository settingsRepository, IDatabaseExportService exportService)
    {
        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
    }

    /// <inheritdoc />
    public async Task<UserSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            double threshold = await _settingsRepository.GetSettingAsync<double>(KeyConfidenceThreshold, cancellationToken).ConfigureAwait(false);
            if (threshold <= 0.0 || threshold > 1.0)
            {
                threshold = 0.65;
            }

            string? device = await _settingsRepository.GetSettingAsync<string>(KeyPreferredDevice, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(device))
            {
                device = "Cpu";
            }

            string? defaultPage = await _settingsRepository.GetSettingAsync<string>(KeyDefaultStartupPage, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(defaultPage))
            {
                defaultPage = "Dashboard";
            }

            bool skipDashboard = await _settingsRepository.GetSettingAsync<bool>(KeySkipDashboardOnStartup, cancellationToken).ConfigureAwait(false);

            string? reportPath = await _settingsRepository.GetSettingAsync<string>(KeyReportExportPath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                reportPath = KnownFolderPaths.GetReportExportDirectory();
            }

            string? dbPath = await _settingsRepository.GetSettingAsync<string>(KeyDatabaseExportPath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                dbPath = _exportService.DefaultExportDirectory;
            }

            string? imagePath = await _settingsRepository.GetSettingAsync<string>(KeyImageOutputDirectory, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                imagePath = await _settingsRepository.GetSettingAsync<string>(LegacyCropOutputDirectoryKey, cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(imagePath))
            {
                imagePath = KnownFolderPaths.GetCropImagesDirectory();
            }

            return new UserSettings
            {
                ConfidenceThreshold = threshold,
                PreferredDevice = device,
                DefaultStartupPage = defaultPage,
                SkipDashboardOnStartup = skipDashboard,
                ReportExportPath = reportPath,
                DatabaseExportPath = dbPath,
                ImageOutputDirectory = imagePath
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Failed to load user settings from repository; utilizing default configuration: {Message}", ex.Message);
            return new UserSettings
            {
                ReportExportPath = KnownFolderPaths.GetReportExportDirectory(),
                DatabaseExportPath = _exportService.DefaultExportDirectory,
                ImageOutputDirectory = KnownFolderPaths.GetCropImagesDirectory()
            };
        }
    }

    /// <inheritdoc />
    public async Task SaveSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _settingsRepository.SetSettingAsync(KeyConfidenceThreshold, settings.ConfidenceThreshold, cancellationToken).ConfigureAwait(false);
        await _settingsRepository.SetSettingAsync(KeyPreferredDevice, settings.PreferredDevice, cancellationToken).ConfigureAwait(false);
        await _settingsRepository.SetSettingAsync(KeyDefaultStartupPage, settings.DefaultStartupPage, cancellationToken).ConfigureAwait(false);
        await _settingsRepository.SetSettingAsync(KeySkipDashboardOnStartup, settings.SkipDashboardOnStartup, cancellationToken).ConfigureAwait(false);
        await _settingsRepository.SetSettingAsync(KeyReportExportPath, settings.ReportExportPath, cancellationToken).ConfigureAwait(false);
        await _settingsRepository.SetSettingAsync(KeyDatabaseExportPath, settings.DatabaseExportPath, cancellationToken).ConfigureAwait(false);
        await _settingsRepository.SetSettingAsync(KeyImageOutputDirectory, settings.ImageOutputDirectory, cancellationToken).ConfigureAwait(false);

        Log.Information("User settings saved successfully. Threshold: {Threshold}, Device: {Device}, Startup: {Page}",
            settings.ConfidenceThreshold, settings.PreferredDevice, settings.DefaultStartupPage);

        // Reactive broadcasts across the application
        WeakReferenceMessenger.Default.Send(new UserSettingsChangedMessage(settings));
        WeakReferenceMessenger.Default.Send(new ConfidenceThresholdChangedMessage(settings.ConfidenceThreshold));
        WeakReferenceMessenger.Default.Send(new DevicePreferenceChangedMessage(settings.PreferredDevice));
    }
}
