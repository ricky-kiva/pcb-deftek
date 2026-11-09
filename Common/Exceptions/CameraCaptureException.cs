using System;

namespace pcb_deftek.Common.Exceptions;

/// <summary>
/// Exception thrown when a camera device cannot be accessed, a video stream is interrupted, or frame capture times out.
/// </summary>
public class CameraCaptureException : HardwareDeviceException
{
    /// <summary>
    /// Gets the zero-based camera index involved in the failure, if applicable.
    /// </summary>
    public int? CameraIndex { get; }

    public CameraCaptureException(string message, int? cameraIndex = null, Exception? innerException = null)
        : base(message, cameraIndex?.ToString(), innerException)
    {
        CameraIndex = cameraIndex;
    }
}
