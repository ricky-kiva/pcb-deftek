using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Infrastructure.Services;

/// <summary>
/// Event arguments holding a captured live camera frame and timestamp metadata.
/// </summary>
public sealed class CameraFrameEventArgs : EventArgs
{
    /// <summary>
    /// Gets the OpenCV Mat representing the captured video frame.
    /// </summary>
    public Mat Frame { get; }

    /// <summary>
    /// Gets the UTC timestamp when the frame was acquired.
    /// </summary>
    public DateTime TimestampUtc { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CameraFrameEventArgs"/> class.
    /// </summary>
    /// <param name="frame">The captured image frame matrix.</param>
    /// <param name="timestampUtc">Optional acquisition timestamp; defaults to <see cref="DateTime.UtcNow"/>.</param>
    public CameraFrameEventArgs(Mat frame, DateTime? timestampUtc = null)
    {
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        TimestampUtc = timestampUtc ?? DateTime.UtcNow;
    }
}

/// <summary>
/// Service abstraction for enumerating video input hardware, managing live stream lifecycles,
/// and streaming OpenCV video frames off the UI thread.
/// </summary>
public interface ICameraService : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets a value indicating whether live frame capture is actively running.
    /// </summary>
    bool IsCapturing { get; }

    /// <summary>
    /// Gets metadata for the currently active camera device, or null if idle.
    /// </summary>
    CameraDeviceInfo? CurrentCamera { get; }

    /// <summary>
    /// Event raised when a new live video frame is captured from the active camera device.
    /// </summary>
    event EventHandler<CameraFrameEventArgs>? FrameReceived;

    /// <summary>
    /// Enumerates all accessible video capture devices connected to the system.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only collection of available camera devices.</returns>
    Task<IReadOnlyList<CameraDeviceInfo>> GetAvailableCamerasAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts asynchronous frame acquisition from the camera matching the specified device index.
    /// </summary>
    /// <param name="cameraIndex">Zero-based hardware device index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StartCaptureAsync(int cameraIndex = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops live frame capture and safely releases the video capture device.
    /// </summary>
    Task StopCaptureAsync();
}
