using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using pcb_deftek.Infrastructure.Logging;
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

    protected override void OnStartup(StartupEventArgs e)
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
