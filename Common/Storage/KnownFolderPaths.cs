using System;
using System.IO;

namespace pcb_deftek.Common.Storage;

/// <summary>
/// Resolves well-known user folders shared by export and crop features.
/// </summary>
public static class KnownFolderPaths
{
    /// <summary>
    /// Gets the application-owned directory under Downloads for exports and crops.
    /// </summary>
    public static string GetApplicationDownloadsDirectory() =>
        Path.Combine(GetDownloadsDirectory(), "PCB Deftek");

    /// <summary>
    /// Gets the default directory for cropped inspection images.
    /// </summary>
    public static string GetCropImagesDirectory() =>
        Path.Combine(GetApplicationDownloadsDirectory(), "Images");

    /// <summary>
    /// Gets the default directory for exported PDF reports.
    /// </summary>
    public static string GetReportExportDirectory() =>
        Path.Combine(GetApplicationDownloadsDirectory(), "Report");

    /// <summary>
    /// Gets the default directory for database backup snapshots.
    /// </summary>
    public static string GetDatabaseBackupDirectory() =>
        Path.Combine(GetApplicationDownloadsDirectory(), "Backup");

    /// <summary>
    /// Gets the user's Downloads directory, falling back to Documents\Downloads when the
    /// user profile cannot be resolved.
    /// </summary>
    public static string GetDownloadsDirectory()
    {
        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                return Path.Combine(userProfile, "Downloads");
            }
        }
        catch
        {
            // Fall back below.
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Downloads");
    }
}
