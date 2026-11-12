using System;
using System.Threading;
using System.Threading.Tasks;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Infrastructure.Inference;

/// <summary>
/// Event arguments delivered when a live camera frame has been processed and annotated.
/// </summary>
public sealed class LiveFrameProcessedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the detection and visualization result of the processed frame.
    /// </summary>
    public LiveFrameResult Result { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveFrameProcessedEventArgs"/> class.
    /// </summary>
    /// <param name="result">The processed frame result.</param>
    public LiveFrameProcessedEventArgs(LiveFrameResult result)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }
}

/// <summary>
/// Service contract for orchestrating real-time camera frame consumption, background ONNX inference,
/// overlay annotation, stale-frame throttling, and live telemetry tracking.
/// </summary>
public interface ILiveInspectionEngine : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets a value indicating whether the live inspection engine is actively processing frames.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets or sets the confidence threshold used for live defect detection (0.10 to 0.95).
    /// </summary>
    double ConfidenceThreshold { get; set; }

    /// <summary>
    /// Gets the current telemetry snapshot (latency, FPS, device name).
    /// </summary>
    LiveInspectionTelemetry CurrentTelemetry { get; }

    /// <summary>
    /// Gets the most recently processed frame result, or null if idle.
    /// </summary>
    LiveFrameResult? LatestResult { get; }

    /// <summary>
    /// Event raised whenever a live camera frame is processed and annotated.
    /// </summary>
    event EventHandler<LiveFrameProcessedEventArgs>? FrameProcessed;

    /// <summary>
    /// Starts the background frame processing and telemetry monitoring loop.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the background frame processing loop and cleans up active frames.
    /// </summary>
    Task StopAsync();
}
