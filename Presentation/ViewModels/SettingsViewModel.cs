using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using pcb_deftek.Common.Storage;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Inference;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence.DatabaseExport;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using Serilog;

namespace pcb_deftek.Presentation.ViewModels;

/// <summary>
/// View model backing the Settings and Attribution dialog panel (UI-INITIAL §7).
/// Manages inference thresholds, hardware device selection, startup routing preferences,
/// export location configurations, and manual database export actions.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IUserSettingsService _settingsService;
    private readonly IDatabaseExportService _exportService;
    private readonly INotificationService _notificationService;
    private readonly IOnnxModelRunner? _modelRunner;
    private readonly Action<string> _urlLauncher;

    public const string ModelName = "pcb-defect-yolov8m-dspcbsd";
    public const string ModelUrl = "https://huggingface.co/Janani-V/pcb-defect-yolov8m-dspcbsd";
    public const string ModelDeveloperName = "Janani-V";
    public const string ModelDeveloperUrl = "https://github.com/Janviswa";

    public const string DeveloperName = "Rickyslash";
    public const string DeveloperWebsiteUrl = "https://rickyslash.my.id";
    public const string DeveloperGitHubUrl = "https://github.com/ricky-kiva";
    public const string DeveloperInstagramUrl = "https://instagram.com/r.ckyslash";
    public const string DeveloperLinkedInUrl = "https://linkedin.com/ricky-fadel";
    public const string DeveloperKofiUrl = "https://ko-fi.com/rickyslash";

    /// <summary>
    /// Event raised when the dialog requests dismissal (e.g. Save or Cancel clicked).
    /// </summary>
    public event EventHandler? RequestClose;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfidencePercentageText))]
    private double _confidenceThreshold = 0.25;

    public string ConfidencePercentageText => $"{Math.Round(ConfidenceThreshold * 100):F0}%";

    [ObservableProperty]
    private string _selectedDevice = "Cpu";

    [ObservableProperty]
    private string _selectedStartupPage = "Dashboard";

    [ObservableProperty]
    private bool _skipDashboardOnStartup;

    [ObservableProperty]
    private string _reportExportPath = string.Empty;

    [ObservableProperty]
    private string _databaseExportPath = string.Empty;

    [ObservableProperty]
    private string _cropOutputDirectory = string.Empty;

    [ObservableProperty]
    private bool _isExportingDatabase;

    [ObservableProperty]
    private string _exportStatusMessage = string.Empty;

    /// <summary>
    /// Gets the list of available inference execution devices.
    /// </summary>
    public IReadOnlyList<string> AvailableDevices { get; }

    /// <summary>
    /// Gets the list of available initial startup pages.
    /// </summary>
    public IReadOnlyList<string> AvailableStartupPages { get; } = new[]
    {
        "Dashboard",
        "Live Detection",
        "File-Based Detection",
        "History"
    };

    /// <inheritdoc />
    public override string Title => "Settings & Attribution";

    /// <summary>
    /// Initializes a new instance of <see cref="SettingsViewModel"/>.
    /// </summary>
    public SettingsViewModel(
        IUserSettingsService settingsService,
        IDatabaseExportService exportService,
        INotificationService notificationService,
        IOnnxModelRunner? modelRunner = null,
        Action<string>? urlLauncher = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _modelRunner = modelRunner;
        _urlLauncher = urlLauncher ?? DefaultUrlLauncher;

        AvailableDevices = DetectAvailableDevices();
    }

    /// <summary>
    /// Loads settings asynchronously from storage and populates presentation properties.
    /// </summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        var settings = await _settingsService.LoadSettingsAsync().ConfigureAwait(false);

        ConfidenceThreshold = settings.ConfidenceThreshold;
        SelectedDevice = settings.PreferredDevice;
        SelectedStartupPage = settings.DefaultStartupPage;
        SkipDashboardOnStartup = settings.SkipDashboardOnStartup;
        ReportExportPath = settings.ReportExportPath;
        DatabaseExportPath = settings.DatabaseExportPath;
        CropOutputDirectory = settings.CropOutputDirectory;
        ExportStatusMessage = string.Empty;
    }

    /// <summary>
    /// Saves current presentation properties to persistent storage and closes the dialog.
    /// </summary>
    [RelayCommand]
    public async Task SaveAsync()
    {
        var settings = new UserSettings
        {
            ConfidenceThreshold = Math.Clamp(Math.Round(ConfidenceThreshold, 2), 0.05, 1.00),
            PreferredDevice = SelectedDevice,
            DefaultStartupPage = SelectedStartupPage,
            SkipDashboardOnStartup = SkipDashboardOnStartup,
            ReportExportPath = string.IsNullOrWhiteSpace(ReportExportPath)
                ? KnownFolderPaths.GetReportExportDirectory()
                : ReportExportPath,
            DatabaseExportPath = string.IsNullOrWhiteSpace(DatabaseExportPath)
                ? KnownFolderPaths.GetDatabaseBackupDirectory()
                : DatabaseExportPath,
            CropOutputDirectory = string.IsNullOrWhiteSpace(CropOutputDirectory)
                ? KnownFolderPaths.GetCropImagesDirectory()
                : CropOutputDirectory
        };

        await _settingsService.SaveSettingsAsync(settings).ConfigureAwait(false);
        Close();
    }

    /// <summary>
    /// Triggers an immediate point-in-time SQLite database export to the configured directory.
    /// </summary>
    [RelayCommand]
    public async Task ExportDatabaseAsync()
    {
        if (IsExportingDatabase)
        {
            return;
        }

        IsExportingDatabase = true;
        ExportStatusMessage = "Exporting database snapshot...";

        try
        {
            string targetDir = string.IsNullOrWhiteSpace(DatabaseExportPath)
                ? _exportService.DefaultExportDirectory
                : DatabaseExportPath;

            string exportedPath = await _exportService.ExportDatabaseAsync(targetDir).ConfigureAwait(false);
            string fileName = Path.GetFileName(exportedPath);

            ExportStatusMessage = $"Export succeeded: {fileName}";
            Log.Information("Database manually exported from settings to '{ExportedPath}'", exportedPath);

            await _notificationService.ShowToastAsync(
                "Database Export Complete",
                $"Successfully exported database snapshot to {fileName}.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ExportStatusMessage = "Export failed. Check application logs.";
            Log.Error(ex, "Manual database export failed from Settings dialog: {Message}", ex.Message);
        }
        finally
        {
            IsExportingDatabase = false;
        }
    }

    /// <summary>
    /// Closes the settings dialog.
    /// </summary>
    [RelayCommand]
    public void Close()
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Safely launches an external hyperlink in the default browser.
    /// </summary>
    [RelayCommand]
    public void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            Log.Warning("Rejected non-HTTPS external URL launch request from Settings: '{Url}'", url);
            return;
        }

        try
        {
            Log.Information("Launching external attribution URL: '{Url}'", url);
            _urlLauncher(url);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to launch external browser for URL '{Url}': {Message}", url, ex.Message);
        }
    }

    private static IReadOnlyList<string> DetectAvailableDevices()
    {
        // Enumerate CPU and GPU execution capabilities
        return new[] { "Cpu", "GpuDirectML" };
    }

    private static void DefaultUrlLauncher(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
