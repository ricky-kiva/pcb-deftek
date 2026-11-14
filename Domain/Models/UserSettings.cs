using System;

namespace pcb_deftek.Domain.Models;

/// <summary>
/// Immutable record capturing all user-configurable runtime options, preferences, and export paths.
/// </summary>
public record UserSettings
{
    /// <summary>
    /// Gets the minimum confidence threshold required for a defect prediction to be retained (0.0 to 1.0).
    /// </summary>
    public double ConfidenceThreshold { get; init; } = 0.25;

    /// <summary>
    /// Gets the preferred inference execution device ("Cpu" or "GpuDirectML").
    /// </summary>
    public string PreferredDevice { get; init; } = "Cpu";

    /// <summary>
    /// Gets the default page to navigate to when the application launches ("Dashboard", "Live Detection", etc.).
    /// </summary>
    public string DefaultStartupPage { get; init; } = "Dashboard";

    /// <summary>
    /// Gets a value indicating whether the application should bypass the Dashboard and navigate directly to Live Detection on startup.
    /// </summary>
    public bool SkipDashboardOnStartup { get; init; } = false;

    /// <summary>
    /// Gets the directory where PDF audit reports should be exported.
    /// </summary>
    public string ReportExportPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the directory where SQLite database backups should be exported.
    /// </summary>
    public string DatabaseExportPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the directory where cropped inspection images should be written.
    /// </summary>
    public string CropOutputDirectory { get; init; } = string.Empty;
}
