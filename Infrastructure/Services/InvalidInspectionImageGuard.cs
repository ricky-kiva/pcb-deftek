using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace pcb_deftek.Infrastructure.Services;

/// <summary>
/// Guard for automatic cleanup of inspections whose images cannot be read. Deletion is only
/// attempted when the image's parent directory is reachable; a disconnected volume or an
/// inaccessible folder must never trigger mass record deletion.
/// </summary>
internal static class InvalidInspectionImageGuard
{
    /// <summary>
    /// Returns whether the parent directory of <paramref name="imagePath"/> can be enumerated.
    /// Missing parents, invalid paths, and inaccessible/unavailable volumes return <see langword="false"/>.
    /// </summary>
    public static bool IsParentDirectoryAccessible(string imagePath, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return false;
        }

        string? parent;
        try
        {
            parent = Path.GetDirectoryName(Path.GetFullPath(imagePath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            logger.LogWarning(ex, "Skipped invalid-image cleanup for an invalid path: {ImagePath}", imagePath);
            return false;
        }

        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            logger.LogWarning(
                "Skipped invalid-image cleanup because the parent directory is unavailable: {ImagePath}",
                imagePath);
            return false;
        }

        try
        {
            using var entries = Directory.EnumerateFileSystemEntries(parent).GetEnumerator();
            _ = entries.MoveNext();
            return true;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or System.Security.SecurityException)
        {
            logger.LogWarning(
                ex,
                "Skipped invalid-image cleanup because the parent directory is inaccessible: {ImagePath}",
                imagePath);
            return false;
        }
    }
}
