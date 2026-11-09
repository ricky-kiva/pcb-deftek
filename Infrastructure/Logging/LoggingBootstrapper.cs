using System;
using System.IO;
using Serilog;

namespace pcb_deftek.Infrastructure.Logging;

/// <summary>
/// Provides centralized configuration and lifecycle management for application-wide structured logging.
/// </summary>
public static class LoggingBootstrapper
{
    private const int DefaultRetainedFileCount = 30;
    private const string LogFilePattern = "app-.log";
    private const string FileOutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";
    private const string DebugOutputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Gets the default directory used for persisting application log files under %LOCALAPPDATA%.
    /// </summary>
    public static string GetDefaultLogDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "PcbDeftek", "pcb-deftek", "logs");
    }

    /// <summary>
    /// Gets the default log file path pattern for rolling log files.
    /// </summary>
    public static string GetDefaultLogFilePath()
    {
        return Path.Combine(GetDefaultLogDirectory(), LogFilePattern);
    }

    /// <summary>
    /// Initializes Serilog structured logging with rolling file and debug sinks.
    /// </summary>
    /// <param name="customLogDirectory">Optional custom directory override for testing.</param>
    public static void Initialize(string? customLogDirectory = null)
    {
        string logDirectory = string.IsNullOrWhiteSpace(customLogDirectory)
            ? GetDefaultLogDirectory()
            : customLogDirectory;

        Directory.CreateDirectory(logDirectory);

        string logFilePath = Path.Combine(logDirectory, LogFilePattern);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Debug(outputTemplate: DebugOutputTemplate)
            .WriteTo.File(
                path: logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: DefaultRetainedFileCount,
                outputTemplate: FileOutputTemplate)
            .CreateLogger();
    }

    /// <summary>
    /// Flushes and closes the active Serilog logger.
    /// </summary>
    public static void Shutdown()
    {
        Log.CloseAndFlush();
    }
}
