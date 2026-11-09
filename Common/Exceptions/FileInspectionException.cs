using System;

namespace pcb_deftek.Common.Exceptions;

/// <summary>
/// Exception thrown when an image or video file cannot be opened, decoded, or processed during file-based detection.
/// </summary>
public class FileInspectionException : InspectionException
{
    /// <summary>
    /// Gets the media file format or extension associated with the failure (e.g., .png, .mp4).
    /// </summary>
    public string? Format { get; }

    public FileInspectionException(string message, string? targetPath = null, string? format = null, Exception? innerException = null)
        : base(message, targetPath, innerException)
    {
        Format = format;
    }
}
