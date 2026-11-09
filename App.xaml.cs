using System;
using System.Runtime.InteropServices;
using System.Windows;
using pcb_deftek.Infrastructure.Logging;
using Serilog;

namespace pcb_deftek;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LoggingBootstrapper.Initialize();
        Log.Information("pcb-deftek application initialized. OS: {OSVersion}, Architecture: {Architecture}",
            Environment.OSVersion,
            RuntimeInformation.ProcessArchitecture);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("pcb-deftek application exiting. ExitCode: {ExitCode}", e.ApplicationExitCode);
        LoggingBootstrapper.Shutdown();

        base.OnExit(e);
    }
}
