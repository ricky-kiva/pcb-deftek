using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Infrastructure.Services;

/// <summary>
/// Default implementation of <see cref="ICameraService"/> wrapping OpenCV's <see cref="VideoCapture"/>
/// for hardware camera enumeration and live background frame streaming.
/// </summary>
public class OpenCvCameraService : ICameraService
{
    private readonly Func<IReadOnlyList<CameraDeviceInfo>> _deviceEnumerator;
    private readonly Func<int, CancellationToken, Task<Mat?>>? _frameSource;
    private readonly ILogger<OpenCvCameraService> _logger;

    private readonly object _syncLock = new();
    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    private VideoCapture? _activeCapture;
    private bool _disposed;

    /// <inheritdoc />
    public bool IsCapturing { get; private set; }

    /// <inheritdoc />
    public CameraDeviceInfo? CurrentCamera { get; private set; }

    /// <inheritdoc />
    public event EventHandler<CameraFrameEventArgs>? FrameReceived;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenCvCameraService"/> class.
    /// </summary>
    /// <param name="deviceEnumerator">Optional custom camera enumerator for unit testing.</param>
    /// <param name="frameSource">Optional custom frame provider function for unit testing.</param>
    /// <param name="logger">Optional logger instance.</param>
    public OpenCvCameraService(
        Func<IReadOnlyList<CameraDeviceInfo>>? deviceEnumerator = null,
        Func<int, CancellationToken, Task<Mat?>>? frameSource = null,
        ILogger<OpenCvCameraService>? logger = null)
    {
        _deviceEnumerator = deviceEnumerator ?? EnumerateConnectedCameras;
        _frameSource = frameSource;
        _logger = logger ?? NullLogger<OpenCvCameraService>.Instance;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CameraDeviceInfo>> GetAvailableCamerasAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            try
            {
                return _deviceEnumerator();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enumerate camera devices.");
                return (IReadOnlyList<CameraDeviceInfo>)Array.Empty<CameraDeviceInfo>();
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task StartCaptureAsync(int cameraIndex = 0, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CameraDeviceInfo targetDevice;
        lock (_syncLock)
        {
            ThrowIfDisposed();

            var cameras = _deviceEnumerator();
            var found = cameras.FirstOrDefault(c => c.Index == cameraIndex);

            if (found == null)
            {
                // In production, if device list was empty or index wasn't found directly, create standard fallback device info
                if (cameras.Count == 0 && _frameSource == null)
                {
                    targetDevice = new CameraDeviceInfo(cameraIndex, $"Camera {cameraIndex}");
                }
                else if (cameras.Count > 0 && found == null)
                {
                    throw new CameraCaptureException($"Camera device with index {cameraIndex} is not available.", cameraIndex);
                }
                else
                {
                    throw new CameraCaptureException($"No camera hardware found for index {cameraIndex}.", cameraIndex);
                }
            }
            else
            {
                targetDevice = found;
            }
        }

        // Stop any running capture before switching
        await StopCaptureAsync().ConfigureAwait(false);

        lock (_syncLock)
        {
            ThrowIfDisposed();

            _captureCts = new CancellationTokenSource();
            CurrentCamera = targetDevice;
            IsCapturing = true;

            var token = _captureCts.Token;

            if (_frameSource != null)
            {
                _captureTask = RunCustomFrameSourceLoopAsync(cameraIndex, token);
            }
            else
            {
                _captureTask = Task.Run(() => RunVideoCaptureLoop(cameraIndex, token), token);
            }

            _logger.LogInformation("Started camera capture on '{CameraName}' (Index {CameraIndex}).", targetDevice.Name, targetDevice.Index);
        }
    }

    /// <inheritdoc />
    public async Task StopCaptureAsync()
    {
        Task? captureTaskToAwait = null;
        CancellationTokenSource? ctsToCancel = null;

        lock (_syncLock)
        {
            if (!IsCapturing && _captureTask == null)
            {
                return;
            }

            IsCapturing = false;
            CurrentCamera = null;

            ctsToCancel = _captureCts;
            _captureCts = null;

            captureTaskToAwait = _captureTask;
            _captureTask = null;
        }

        try
        {
            ctsToCancel?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed
        }

        if (captureTaskToAwait != null)
        {
            try
            {
                await captureTaskToAwait.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception observed while awaiting camera capture shutdown.");
            }
        }

        lock (_syncLock)
        {
            _activeCapture?.Release();
            _activeCapture?.Dispose();
            _activeCapture = null;
            ctsToCancel?.Dispose();
        }

        _logger.LogInformation("Camera capture stopped cleanly.");
    }

    private void RunVideoCaptureLoop(int cameraIndex, CancellationToken cancellationToken)
    {
        VideoCapture? capture = null;
        try
        {
            // Attempt opening via DirectShow first for lower latency on Windows, then fallback to ANY
            capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW);
            if (!capture.IsOpened())
            {
                capture.Dispose();
                capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.ANY);
            }

            if (!capture.IsOpened())
            {
                _logger.LogError("Failed to open OpenCV VideoCapture for camera index {CameraIndex}.", cameraIndex);
                lock (_syncLock)
                {
                    IsCapturing = false;
                    CurrentCamera = null;
                }
                return;
            }

            lock (_syncLock)
            {
                _activeCapture = capture;
            }

            using var frame = new Mat();
            while (!cancellationToken.IsCancellationRequested && capture.IsOpened())
            {
                if (capture.Read(frame) && !frame.Empty())
                {
                    try
                    {
                        FrameReceived?.Invoke(this, new CameraFrameEventArgs(frame.Clone(), DateTime.UtcNow));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Exception in camera frame listener.");
                    }
                }
                else
                {
                    Thread.Sleep(10);
                }

                // Minimal yield to avoid 100% CPU core pinning
                Thread.Sleep(5);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in camera capture loop for camera index {CameraIndex}.", cameraIndex);
        }
        finally
        {
            capture?.Release();
            capture?.Dispose();

            lock (_syncLock)
            {
                if (_activeCapture == capture)
                {
                    _activeCapture = null;
                }
            }
        }
    }

    private async Task RunCustomFrameSourceLoopAsync(int cameraIndex, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await _frameSource!(cameraIndex, cancellationToken).ConfigureAwait(false);
                if (frame != null && !frame.Empty())
                {
                    try
                    {
                        FrameReceived?.Invoke(this, new CameraFrameEventArgs(frame, DateTime.UtcNow));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Exception in custom frame source listener.");
                    }
                }

                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private static IReadOnlyList<CameraDeviceInfo> EnumerateConnectedCameras()
    {
        var devices = new List<CameraDeviceInfo>();
        const int maxCamerasToProbe = 4;

        for (int i = 0; i < maxCamerasToProbe; i++)
        {
            try
            {
                using var cap = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
                if (cap.IsOpened())
                {
                    devices.Add(new CameraDeviceInfo(i, $"Camera {i}"));
                }
                else
                {
                    // If index 0 didn't open with DSHOW, attempt ANY
                    using var anyCap = new VideoCapture(i, VideoCaptureAPIs.ANY);
                    if (anyCap.IsOpened())
                    {
                        devices.Add(new CameraDeviceInfo(i, $"Camera {i}"));
                    }
                    else if (i > 0)
                    {
                        // Hardware indices on Windows are typically sequential
                        break;
                    }
                }
            }
            catch
            {
                if (i > 0)
                {
                    break;
                }
            }
        }

        return devices;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(OpenCvCameraService));
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
            StopCaptureAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore during sync disposal
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopCaptureAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
