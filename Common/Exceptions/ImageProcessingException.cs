using System;

namespace pcb_deftek.Common.Exceptions;

/// <summary>
/// Exception thrown when image manipulation, letterboxing, or defect region cropping fails.
/// </summary>
public class ImageProcessingException : InspectionException
{
    /// <summary>
    /// Gets the specific image processing operation that failed (e.g., Crop, Resize, Normalize).
    /// </summary>
    public string? Operation { get; }

    public ImageProcessingException(string message, string? operation = null, Exception? innerException = null)
        : base(message, targetPath: null, innerException)
    {
        Operation = operation;
    }
}
