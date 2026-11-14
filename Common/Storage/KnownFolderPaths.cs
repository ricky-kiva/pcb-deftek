using System;
using System.IO;

namespace pcb_deftek.Common.Storage;

/// <summary>
/// Resolves well-known user folders shared by export and crop features.
/// </summary>
public static class KnownFolderPaths
{
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
