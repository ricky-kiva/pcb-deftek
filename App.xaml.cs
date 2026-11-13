using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using pcb_deftek.Infrastructure.Logging;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence;
using pcb_deftek.Infrastructure.Persistence.DatabaseBackup;
using pcb_deftek.Infrastructure.Persistence.DatabaseExport;
using pcb_deftek.Infrastructure.Persistence.Migrations;
using pcb_deftek.Infrastructure.Inference;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;
using pcb_deftek.Infrastructure.Vision;
using pcb_deftek.Presentation.Navigation;
using pcb_deftek.Presentation.Services;
using pcb_deftek.Presentation.ViewModels;
using Serilog;
namespace pcb_deftek;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Gets the current application instance strongly typed as <see cref="App"/>.
    /// </summary>
    public static new App Current => (App)Application.Current;

    /// <summary>
    /// Controls whether error dialogs are displayed on unhandled dispatcher exceptions.
    /// Can be disabled in automated test environments.
    /// </summary>
    public static bool ShowErrorDialogs { get; set; } = true;

    /// <summary>
    /// Gets the dependency injection service provider.
    /// </summary>
    public IServiceProvider Services => _serviceProvider
        ?? throw new InvalidOperationException("Service provider has not been initialized.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LoggingBootstrapper.Initialize();
        RegisterGlobalExceptionHandlers();

        Log.Information("pcb-deftek application initialized. OS: {OSVersion}, Architecture: {Architecture}",
            Environment.OSVersion,
            RuntimeInformation.ProcessArchitecture);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Run automated database migrations on startup
        try
        {
            var migrator = _serviceProvider.GetRequiredService<IDatabaseMigrator>();
            await migrator.MigrateToLatestAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Critical database migration failure during application startup: {Message}", ex.Message);
            throw;
        }
        // Evaluate startup preferences and route initial view model
        try
        {
            var router = _serviceProvider.GetRequiredService<IStartupRoutingService>();
            await router.RouteStartupAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Non-critical startup routing evaluation warning: {Message}", ex.Message);
        }

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("pcb-deftek application exiting. ExitCode: {ExitCode}", e.ApplicationExitCode);

        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        LoggingBootstrapper.Shutdown();

        base.OnExit(e);
    }

    /// <summary>
    /// Configures application dependencies in the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection to register dependencies into.</param>
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddSerilog(Log.Logger, dispose: false);
        });

        // Core Persistence Services
        services.AddSingleton<ISqliteConnectionFactory>(_ => new SqliteConnectionFactory());
        services.AddSingleton<IDatabaseMigrator, SqliteDatabaseMigrator>();
        services.AddSingleton<IInspectionRepository, SqliteInspectionRepository>();
        services.AddSingleton<IDefectRepository, SqliteDefectRepository>();
        services.AddSingleton<ISettingsRepository, SqliteSettingsRepository>();
        services.AddSingleton<IUserSettingsService, UserSettingsService>();
        services.AddSingleton<IDatabaseBackupService, SqliteDatabaseBackupService>();
        services.AddSingleton<IDatabaseExportService, SqliteDatabaseExportService>();

        // Notification & File Ingestion Services
        services.AddSingleton<INotificationService, WindowsNotificationService>();
        services.AddSingleton<IFileDiscoveryService, FileDiscoveryService>();
        services.AddSingleton<IFilePickerService, WpfFilePickerService>();
        services.AddSingleton<IBatchInspectionEngine, BatchInspectionEngine>();
        services.AddSingleton<ICameraService, OpenCvCameraService>();
        services.AddSingleton<ILiveInspectionEngine, LiveInspectionEngine>();
        services.AddSingleton<ILiveSnapshotService, LiveSnapshotService>();
        services.AddSingleton<IHistoryThumbnailService, HistoryThumbnailService>();
        services.AddSingleton<IInspectionDeletionService, InspectionDeletionService>();
        services.AddSingleton<IConfirmationService, WpfConfirmationService>();

        // Core Inference & Vision Services
        services.AddSingleton<IOnnxModelRunner, OnnxModelRunner>();
        services.AddSingleton<IVisionPreprocessor, OpenCvVisionPreprocessor>();
        services.AddSingleton<IYoloPostprocessor, YoloPostprocessor>();
        services.AddSingleton<IPcbDefectDetector, PcbDefectDetector>();
        services.AddSingleton<IImageAnnotationService, ImageAnnotationService>();
        // Navigation & Presentation Services
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IStartupRoutingService, StartupRoutingService>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();

        // Page ViewModels
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<LiveDetectionViewModel>();
        services.AddTransient<FileDetectionViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddSingleton<MainWindow>();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled UI dispatcher exception: {Message}", e.Exception.Message);

        if (ShowErrorDialogs)
        {
            MessageBox.Show(
                $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nPlease check the application logs for further details.",
                "Application Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        e.Handled = true;
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Log.Fatal(ex, "Unhandled AppDomain exception encountered. IsTerminating: {IsTerminating}", e.IsTerminating);
        }
        else
        {
            Log.Fatal("Unhandled non-CLS AppDomain exception: {ExceptionObject}", e.ExceptionObject);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved background task exception: {Message}", e.Exception.Message);
        e.SetObserved();
    }
}
