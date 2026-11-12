using System;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using pcb_deftek.Presentation.Navigation;
using Serilog;

namespace pcb_deftek.Presentation.ViewModels;

/// <summary>
/// Root view model for the shell window managing primary sidebar navigation,
/// active workspace view model hosting, and footer attribution interactions.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly Action<string> _urlLauncher;

    /// <summary>
    /// Gets the navigation service managing active child view models.
    /// </summary>
    public INavigationService NavigationService { get; }

    /// <summary>
    /// Gets the settings view model backing the settings modal overlay.
    /// </summary>
    public SettingsViewModel? SettingsViewModel { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the navigation sidebar is expanded or collapsed.
    /// </summary>
    [ObservableProperty]
    private bool _isSidebarExpanded = true;

    /// <summary>
    /// Gets or sets a value indicating whether the settings panel is open.
    /// </summary>
    [ObservableProperty]
    private bool _isSettingsOpen;

    /// <inheritdoc />
    public override string Title => "PCB Deftek";

    /// <summary>
    /// Initializes a new instance of <see cref="MainViewModel"/>.
    /// </summary>
    /// <param name="navigationService">Navigation service responsible for view transitions.</param>
    /// <param name="settingsViewModel">Optional settings view model instance.</param>
    /// <param name="urlLauncher">Optional custom delegate for launching external URLs (useful in testing).</param>
    public MainViewModel(
        INavigationService navigationService,
        SettingsViewModel? settingsViewModel = null,
        Action<string>? urlLauncher = null)
    {
        NavigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        SettingsViewModel = settingsViewModel;
        if (SettingsViewModel != null)
        {
            SettingsViewModel.RequestClose += (_, _) => IsSettingsOpen = false;
        }
        _urlLauncher = urlLauncher ?? DefaultUrlLauncher;
    }

    /// <summary>
    /// Test-convenience constructor specifying a custom URL launcher without settings view model.
    /// </summary>
    public MainViewModel(INavigationService navigationService, Action<string> urlLauncher)
        : this(navigationService, null, urlLauncher)
    {
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarExpanded = !IsSidebarExpanded;
    }

    [RelayCommand]
    private void NavigateToDashboard()
    {
        NavigationService.NavigateTo<DashboardViewModel>();
    }

    [RelayCommand]
    private void NavigateToLiveDetection()
    {
        NavigationService.NavigateTo<LiveDetectionViewModel>();
    }

    [RelayCommand]
    private void NavigateToFileDetection()
    {
        NavigationService.NavigateTo<FileDetectionViewModel>();
    }

    [RelayCommand]
    private void NavigateToHistory()
    {
        NavigationService.NavigateTo<HistoryViewModel>();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        if (SettingsViewModel != null)
        {
            _ = SettingsViewModel.LoadAsync();
        }
        IsSettingsOpen = true;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsOpen = false;
    }

    [RelayCommand]
    private void OpenExternalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        // Security check: restrict external launcher strictly to HTTPS schemes
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            Log.Warning("Rejected non-HTTPS external URL launch request: '{Url}'", url);
            return;
        }

        try
        {
            Log.Information("Launching external URL: '{Url}'", url);
            _urlLauncher(url);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to launch external browser for URL '{Url}': {Message}", url, ex.Message);
        }
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
