using System;
using System.Runtime.InteropServices;
using System.Windows;
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
    /// Gets the dependency injection service provider.
    /// </summary>
    public IServiceProvider Services => _serviceProvider
        ?? throw new InvalidOperationException("Service provider has not been initialized.");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LoggingBootstrapper.Initialize();
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
}
