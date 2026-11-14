using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Services;
using pcb_deftek.Infrastructure.Vision;

namespace pcb_deftek.Infrastructure.Inference;

/// <summary>
/// Default implementation of <see cref="ILiveInspectionEngine"/> coordinating background camera frame consumption,
/// stale-frame dropping, real-time ONNX inference, overlay annotation, and live telemetry tracking.
/// </summary>
public class LiveInspectionEngine : ILiveInspectionEngine
{
    private readonly ICameraService _cameraService;
    private readonly IPcbDefectDetector _detector;
    private readonly IImageAnnotationService _annotationService;
    private readonly ILogger<LiveInspectionEngine> _logger;

    private readonly object _syncLock = new();
    private readonly object _pendingFrameLock = new();
    private readonly SemaphoreSlim _frameSignal = new(0, 1);

    private readonly Queue<DateTime> _fpsTimestamps = new();
    private readonly Queue<double> _latencyHistory = new();
    private readonly Queue<LiveFrameResult> _recentResults = new();

    private Mat? _pendingFrame;
    private CancellationTokenSource? _workerCts;
    private Task? _workerTask;
    private double _confidenceThreshold = 0.65;
    private LiveFrameResult? _latestResult;
    private bool _disposed;

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    public double ConfidenceThreshold
    {
        get => _confidenceThreshold;
        set => _confidenceThreshold = Math.Clamp(value, 0.10, 0.95);
    }

    /// <inheritdoc />
    public LiveInspectionTelemetry CurrentTelemetry { get; private set; } = LiveInspectionTelemetry.Empty;

    /// <inheritdoc />
    public LiveFrameResult? LatestResult
    {
        get
        {
            lock (_syncLock)
            {
                return _latestResult;
            }
        }
        private set
        {
            lock (_syncLock)
            {
                _latestResult = value;
                if (value != null)
                {
                    _recentResults.Enqueue(value);
                    while (_recentResults.Count > 3)
                    {
                        var expired = _recentResults.Dequeue();
                        expired.Dispose();
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<LiveFrameProcessedEventArgs>? FrameProcessed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveInspectionEngine"/> class.
    /// </summary>
    public LiveInspectionEngine(
        ICameraService cameraService,
        IPcbDefectDetector detector,
        IImageAnnotationService annotationService,
        ILogger<LiveInspectionEngine>? logger = null)
    {
        _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _annotationService = annotationService ?? throw new ArgumentNullException(nameof(annotationService));
        _logger = logger ?? NullLogger<LiveInspectionEngine>.Instance;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncLock)
        {
            ThrowIfDisposed();

            if (IsRunning)
            {
                return Task.CompletedTask;
            }

            IsRunning = true;
            _workerCts = new CancellationTokenSource();
            _cameraService.FrameReceived += OnCameraFrameReceived;

            var token = _workerCts.Token;
            _workerTask = Task.Run(() => RunProcessingLoopAsync(token), token);

            _logger.LogInformation("Live inspection engine started.");
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        Task? taskToAwait = null;
        CancellationTokenSource? ctsToCancel = null;

        lock (_syncLock)
        {
            if (!IsRunning && _workerTask == null)
            {
                return;
            }

            IsRunning = false;
            _cameraService.FrameReceived -= OnCameraFrameReceived;

            ctsToCancel = _workerCts;
            _workerCts = null;

            taskToAwait = _workerTask;
            _workerTask = null;
        }

        try
        {
            ctsToCancel?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed
        }

        // Release signal so waiting worker loop unblocks immediately
        try
        {
            if (_frameSignal.CurrentCount == 0)
            {
                _frameSignal.Release();
            }
        }
        catch (ObjectDisposedException)
        {
            // Already disposed
        }

        if (taskToAwait != null)
        {
            try
            {
                await taskToAwait.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception awaiting live inspection engine worker shutdown.");
            }
        }

        lock (_pendingFrameLock)
        {
            _pendingFrame?.Dispose();
            _pendingFrame = null;
        }

        lock (_syncLock)
        {
            while (_recentResults.Count > 0)
            {
                _recentResults.Dequeue().Dispose();
            }

            _latestResult = null;
            CurrentTelemetry = LiveInspectionTelemetry.Empty;
            _fpsTimestamps.Clear();
            _latencyHistory.Clear();
            ctsToCancel?.Dispose();
        }

        _logger.LogInformation("Live inspection engine stopped cleanly.");
    }

    private void OnCameraFrameReceived(object? sender, CameraFrameEventArgs e)
    {
        if (!IsRunning || e.Frame == null || e.Frame.Empty())
        {
            return;
        }

        lock (_pendingFrameLock)
        {
            // Stale frame drop: if a frame is already queued and waiting for worker, dispose it immediately
            _pendingFrame?.Dispose();
            _pendingFrame = e.Frame.Clone();
        }

        try
        {
            if (_frameSignal.CurrentCount == 0)
            {
                _frameSignal.Release();
            }
        }
        catch (ObjectDisposedException)
        {
            // Disposed during shutdown
        }
    }

    private async Task RunProcessingLoopAsync(CancellationToken cancellationToken)
    {
        var sw = new Stopwatch();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _frameSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            Mat? frameToProcess;
            lock (_pendingFrameLock)
            {
                frameToProcess = _pendingFrame;
                _pendingFrame = null;
            }

            if (frameToProcess == null || frameToProcess.Empty())
            {
                continue;
            }

            using (frameToProcess)
            {
                try
                {
                    sw.Restart();
                    var inference = await _detector.DetectAsync(
                        frameToProcess,
                        confidenceThreshold: (float)ConfidenceThreshold,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    sw.Stop();

                    double latencyMs = sw.Elapsed.TotalMilliseconds;
                    string deviceName = inference.Device == ExecutionDevice.GpuDirectML ? "DirectML GPU" : "CPU";
                    var telemetry = CalculateTelemetry(latencyMs, deviceName);
                    // Render defect bounding boxes directly onto in-memory frame
                    var annotatedBitmap = _annotationService.AnnotateMat(frameToProcess, inference.Defects)
                        ?? _annotationService.MatToBitmapSource(frameToProcess);

                    if (annotatedBitmap == null)
                    {
                        continue;
                    }

                    var result = new LiveFrameResult(
                        annotatedBitmap,
                        frameToProcess.Clone(),
                        inference,
                        telemetry);

                    LatestResult = result;
                    CurrentTelemetry = telemetry;

                    FrameProcessed?.Invoke(this, new LiveFrameProcessedEventArgs(result));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing live camera frame.");
                }
            }
        }
    }

    private LiveInspectionTelemetry CalculateTelemetry(double latencyMs, string deviceName)
    {
        lock (_syncLock)
        {
            var now = DateTime.UtcNow;

            // Rolling FPS calculation over 1-second window
            _fpsTimestamps.Enqueue(now);
            while (_fpsTimestamps.Count > 0 && (now - _fpsTimestamps.Peek()).TotalSeconds > 1.0)
            {
                _fpsTimestamps.Dequeue();
            }
            double fps = _fpsTimestamps.Count;

            // Rolling average latency calculation over recent frames
            _latencyHistory.Enqueue(latencyMs);
            while (_latencyHistory.Count > 30)
            {
                _latencyHistory.Dequeue();
            }
            double avgLatency = _latencyHistory.Count > 0 ? _latencyHistory.Average() : latencyMs;

            return new LiveInspectionTelemetry(
                InferenceLatencyMs: Math.Round(latencyMs, 1),
                AverageLatencyMs: Math.Round(avgLatency, 1),
                Fps: fps,
                DeviceName: deviceName,
                TimestampUtc: now);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LiveInspectionEngine));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore during sync disposal
        }

        _frameSignal.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        _frameSignal.Dispose();
        GC.SuppressFinalize(this);
    }
}
