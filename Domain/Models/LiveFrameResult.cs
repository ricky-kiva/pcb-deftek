using System;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using pcb_deftek.Infrastructure.Inference;

namespace pcb_deftek.Domain.Models;

/// <summary>
/// Encapsulates the complete detection and visualization result of a single live camera frame.
/// </summary>
public sealed class LiveFrameResult : IDisposable
{
    private readonly object _frameLock = new();
    private bool _disposed;

    /// <summary>
    /// Gets the thread-safe frozen WPF <see cref="BitmapSource"/> with rendered defect bounding boxes and labels.
    /// </summary>
    public BitmapSource AnnotatedImage { get; }

    /// <summary>
    /// Gets the cloned raw OpenCV <see cref="Mat"/> of the original camera frame without annotations.
    /// </summary>
    public Mat RawFrame { get; }

    /// <summary>
    /// Gets the ONNX model inference result containing detected defects, bounding boxes, and letterbox geometry.
    /// </summary>
    public InferenceResult Inference { get; }

    /// <summary>
    /// Gets the telemetry snapshot captured for this processed frame.
    /// </summary>
    public LiveInspectionTelemetry Telemetry { get; }

    /// <summary>
    /// Gets the total number of detected defects on this frame.
    /// </summary>
    public int DefectCount => Inference.Defects.Count;

    /// <summary>
    /// Gets a value indicating whether this frame passed inspection (clean, 0 defects).
    /// </summary>
    public bool IsPass => DefectCount == 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveFrameResult"/> class.
    /// </summary>
    public LiveFrameResult(
        BitmapSource annotatedImage,
        Mat rawFrame,
        InferenceResult inference,
        LiveInspectionTelemetry telemetry)
    {
        AnnotatedImage = annotatedImage ?? throw new ArgumentNullException(nameof(annotatedImage));
        RawFrame = rawFrame ?? throw new ArgumentNullException(nameof(rawFrame));
        Inference = inference ?? throw new ArgumentNullException(nameof(inference));
        Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    }

    /// <summary>
    /// Creates a thread-safe deep clone of <see cref="RawFrame"/> if it has not been disposed.
    /// </summary>
    /// <returns>A cloned <see cref="Mat"/> instance, or null if already disposed.</returns>
    public Mat? TryCloneRawFrame()
    {
        lock (_frameLock)
        {
            if (_disposed || RawFrame.IsDisposed || RawFrame.Empty())
            {
                return null;
            }

            return RawFrame.Clone();
        }
    }

    /// <summary>
    /// Disposes the unmanaged raw frame matrix thread-safely.
    /// </summary>
    public void Dispose()
    {
        lock (_frameLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            RawFrame.Dispose();
        }
    }
}
