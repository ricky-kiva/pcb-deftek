using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pcb_deftek.Domain.Models;
using pcb_deftek.Domain.Models.Messages;
using pcb_deftek.Infrastructure.Inference;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;

namespace pcb_deftek.Presentation.ViewModels;

/// <summary>
/// View model backing the real-time live camera feed, defect overlay rendering,
/// telemetry monitoring, dynamic camera switching, and one-touch snapshot capture (Phase 6).
/// </summary>
public partial class LiveDetectionViewModel : ViewModelBase
{
    private readonly ICameraService _cameraService;
    private readonly ILiveInspectionEngine _liveInspectionEngine;
    private readonly ILiveSnapshotService _snapshotService;
    private readonly IUserSettingsService _userSettingsService;
    private readonly INotificationService _notificationService;
    private readonly IMessenger _messenger;
    private readonly ILogger<LiveDetectionViewModel> _logger;
    private readonly object _camerasLock = new();

    private bool _suppressCameraSwitch;
    private CancellationTokenSource? _navigationCts;
    private Task? _startupTask;
    private int _navigationVersion;

    private LiveFrameResult? _pendingFrameResult;
    private int _frameDispatchScheduled;

    /// <summary>
    /// Gets or sets an override used to resolve the UI dispatcher. Tests supply a dispatcher so
    /// UI-marshalling behaviour can be exercised without a running <see cref="System.Windows.Application"/>.
    /// </summary>
    internal Func<System.Windows.Threading.Dispatcher?>? UiDispatcherProvider { get; set; }

    /// <inheritdoc />
    public override string Title => "Live Detection";

    /// <summary>
    /// Gets the collection of discovered camera hardware devices.
    /// </summary>
    public ObservableCollection<CameraDeviceInfo> AvailableCameras { get; } = new();

    [ObservableProperty]
    private CameraDeviceInfo? _selectedCamera;

    [ObservableProperty]
    private BitmapSource? _currentFrameImage;

    [ObservableProperty]
    private string _statusText = "Camera Idle";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCaptureSnapshot))]
    [NotifyCanExecuteChangedFor(nameof(CaptureSnapshotCommand))]
    private bool _isStreaming;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCaptureSnapshot))]
    [NotifyCanExecuteChangedFor(nameof(CaptureSnapshotCommand))]
    private bool _isCapturingSnapshot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBadgeText))]
    private bool _isPass = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefectCountText))]
    private int _defectCount;

    [ObservableProperty]
    private double _inferenceLatencyMs;

    [ObservableProperty]
    private double _fps;

    [ObservableProperty]
    private string _deviceName = "CPU";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TelemetryCardOpacity))]
    private bool _isTelemetryEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfigurationControlsOpacity))]
    private bool _isControlsAutoHidden;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfidencePercentageText))]
    private double _confidenceThreshold = 0.65;

    [ObservableProperty]
    private InspectionRecord? _lastCapturedRecord;

    /// <summary>
    /// Gets the live status badge text ("PASS" or "FAIL").
    /// </summary>
    public string StatusBadgeText => IsPass ? "PASS" : "FAIL";

    /// <summary>
    /// Gets the formatted defect counter string ("0 Defects", "1 Defect", "N Defects").
    /// </summary>
    public string DefectCountText => DefectCount == 1 ? "1 Defect" : $"{DefectCount} Defects";

    /// <summary>
    /// Gets the formatted confidence percentage string (e.g., "25%").
    /// </summary>
    public string ConfidencePercentageText => $"{ConfidenceThreshold * 100.0:F0}%";

    /// <summary>
    /// Gets a value indicating whether a live frame snapshot can be captured.
    /// </summary>
    public bool CanCaptureSnapshot => IsStreaming && !IsCapturingSnapshot;


    /// <summary>
    /// Gets the opacity of the persistent status card (PASS/FAIL badge + defect counter). Always 1.0.
    /// </summary>
    public double StatusCardOpacity => 1.0;

    /// <summary>
    /// Gets the opacity of the telemetry readout card (visible while telemetry is enabled, independent of cursor auto-hide).
    /// </summary>
    public double TelemetryCardOpacity => IsTelemetryEnabled ? 1.0 : 0.0;

    /// <summary>
    /// Gets the opacity of the configuration controls (camera selector, confidence slider, capture button).
    /// </summary>
    public double ConfigurationControlsOpacity => IsControlsAutoHidden ? 0.0 : 1.0;
    /// <summary>
    /// Initializes a new instance of the <see cref="LiveDetectionViewModel"/> class.
    /// </summary>
    public LiveDetectionViewModel(
        ICameraService cameraService,
        ILiveInspectionEngine liveInspectionEngine,
        ILiveSnapshotService snapshotService,
        IUserSettingsService userSettingsService,
        INotificationService notificationService,
        ILogger<LiveDetectionViewModel>? logger = null,
        IMessenger? messenger = null)
    {
        _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
        _liveInspectionEngine = liveInspectionEngine ?? throw new ArgumentNullException(nameof(liveInspectionEngine));
        _snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
        _userSettingsService = userSettingsService ?? throw new ArgumentNullException(nameof(userSettingsService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _logger = logger ?? NullLogger<LiveDetectionViewModel>.Instance;

        System.Windows.Data.BindingOperations.EnableCollectionSynchronization(AvailableCameras, _camerasLock);

        _messenger.Register<LiveDetectionViewModel, ConfidenceThresholdChangedMessage>(
            this,
            static (recipient, message) => recipient.ConfidenceThreshold = message.Value);
    }

    partial void OnConfidenceThresholdChanged(double value)
    {
        double clamped = Math.Clamp(value, 0.10, 0.95);
        if (Math.Abs(_confidenceThreshold - clamped) > 0.0001)
        {
            _confidenceThreshold = clamped;
        }

        _liveInspectionEngine.ConfidenceThreshold = clamped;
    }

    partial void OnSelectedCameraChanged(CameraDeviceInfo? value)
    {
        if (_suppressCameraSwitch || value == null || !IsStreaming)
        {
            return;
        }

        _ = SwitchCameraAsync(value);
    }

    /// <inheritdoc />
    public override async Task OnNavigatedToAsync()
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _navigationCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        int version = Interlocked.Increment(ref _navigationVersion);

        // Signal shell window to collapse sidebar by default on Live Camera page.
        _messenger.Send(new SidebarCollapseMessage(true));

        Task startup = StartLiveAsync(cts.Token, version);
        _startupTask = startup;
        try
        {
            await startup.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Navigation moved away before initialization completed.
        }
    }

    /// <inheritdoc />
    public override async Task OnNavigatedFromAsync()
    {
        // Restore sidebar to expanded when leaving Live Detection page.
        _messenger.Send(new SidebarCollapseMessage(false));

        Interlocked.Increment(ref _navigationVersion);
        var cts = Interlocked.Exchange(ref _navigationCts, null);
        cts?.Cancel();

        // Remove the frame handler immediately; initialization may still be awaiting hardware.
        _liveInspectionEngine.FrameProcessed -= OnFrameProcessed;

        Task? startup = _startupTask;
        _startupTask = null;
        if (startup is not null)
        {
            try
            {
                await startup.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when navigation cancels initialization.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while waiting for live camera initialization to stop.");
            }
        }

        cts?.Dispose();
        _liveInspectionEngine.FrameProcessed -= OnFrameProcessed;

        try
        {
            await _liveInspectionEngine.StopAsync().ConfigureAwait(false);
            await _cameraService.StopCaptureAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while stopping live camera capture.");
        }
        finally
        {
            UpdateOnUiThread(() =>
            {
                IsStreaming = false;
                StatusText = "Camera Stopped";
            });
        }
    }

    private async Task StartLiveAsync(CancellationToken token, int version)
    {
        try
        {
            UserSettings? settings = null;
            try
            {
                settings = await _userSettingsService
                    .LoadSettingsAsync(token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load user settings into LiveDetectionViewModel.");
            }

            if (!IsCurrentNavigation(version, token))
            {
                return;
            }

            if (settings != null)
            {
                UpdateOnUiThread(() => ConfidenceThreshold = settings.ConfidenceThreshold);
            }

            _liveInspectionEngine.FrameProcessed -= OnFrameProcessed;
            _liveInspectionEngine.FrameProcessed += OnFrameProcessed;

            var cameras = await _cameraService
                .GetAvailableCamerasAsync(token)
                .ConfigureAwait(false);

            if (!IsCurrentNavigation(version, token))
            {
                return;
            }

            UpdateOnUiThread(() =>
            {
                if (version != Volatile.Read(ref _navigationVersion))
                {
                    return;
                }

                _suppressCameraSwitch = true;
                try
                {
                    lock (_camerasLock)
                    {
                        AvailableCameras.Clear();
                        if (cameras != null)
                        {
                            foreach (var cam in cameras)
                            {
                                AvailableCameras.Add(cam);
                            }
                        }

                        if (AvailableCameras.Count == 0)
                        {
                            AvailableCameras.Add(new CameraDeviceInfo(0, "Camera 0"));
                        }

                        SelectedCamera = AvailableCameras[0];
                    }
                }
                finally
                {
                    _suppressCameraSwitch = false;
                }
            });

            if (!IsCurrentNavigation(version, token))
            {
                return;
            }

            // Read the index from the enumerated list rather than from SelectedCamera, which is
            // assigned on the dispatcher thread; this continuation must not depend on that post running first.
            int targetIndex = cameras is { Count: > 0 } ? cameras[0].Index : 0;
            await _liveInspectionEngine
                .StartAsync(token)
                .ConfigureAwait(false);

            if (!IsCurrentNavigation(version, token))
            {
                return;
            }

            await _cameraService
                .StartCaptureAsync(targetIndex, token)
                .ConfigureAwait(false);

            if (!IsCurrentNavigation(version, token))
            {
                return;
            }

            UpdateOnUiThread(() =>
            {
                if (version == Volatile.Read(ref _navigationVersion))
                {
                    IsStreaming = true;
                    StatusText = $"Streaming Live ({SelectedCamera?.Name ?? "Camera 0"})";
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Navigation moved away; OnNavigatedFrom stops any partially started capture.
        }
        catch (Exception ex)
        {
            if (version == Volatile.Read(ref _navigationVersion))
            {
                _logger.LogWarning(ex, "Unable to start live camera capture.");
                UpdateOnUiThread(() =>
                {
                    IsStreaming = false;
                    StatusText = $"Camera unavailable: {ex.Message}";
                });
            }
        }
    }

    /// <summary>
    /// Toggles visibility of the live telemetry readout (latency ms and FPS).
    /// </summary>
    [RelayCommand]
    public void ToggleTelemetry()
    {
        IsTelemetryEnabled = !IsTelemetryEnabled;
    }

    /// <summary>
    /// Marks the cursor as idle and auto-hides configuration controls while preserving status and enabled telemetry.
    /// </summary>
    public void TriggerIdleAutoHide()
    {
        IsControlsAutoHidden = true;
    }

    /// <summary>
    /// Restores full visibility of all floating controls upon user mouse or keyboard activity.
    /// </summary>
    public void NotifyUserActivity()
    {
        IsControlsAutoHidden = false;
    }

    /// <summary>
    /// Captures the current live video frame, saves raw and annotated images, and persists the record to SQLite history.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCaptureSnapshot))]
    public async Task CaptureSnapshotAsync()
    {
        if (!CanCaptureSnapshot)
        {
            return;
        }

        IsCapturingSnapshot = true;
        try
        {
            var record = await _snapshotService.CaptureLatestAsync().ConfigureAwait(false);

            UpdateOnUiThread(() =>
            {
                LastCapturedRecord = record;
                StatusText = $"Captured '{record.Name}' ({record.Status})";
            });

            try
            {
                await _notificationService.ShowToastAsync(
                    "Live Snapshot Captured",
                    $"Saved '{record.Name}' ({record.Status}, {record.DefectCount} defect(s)) to inspection history.").ConfigureAwait(false);
            }
            catch (Exception notifEx)
            {
                _logger.LogWarning(notifEx, "Failed to dispatch snapshot toast notification.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture live camera snapshot.");
            UpdateOnUiThread(() =>
            {
                StatusText = $"Snapshot failed: {ex.Message}";
            });
        }
        finally
        {
            UpdateOnUiThread(() =>
            {
                IsCapturingSnapshot = false;
            });
        }
    }

    private async Task SwitchCameraAsync(CameraDeviceInfo targetCamera)
    {
        try
        {
            UpdateOnUiThread(() =>
            {
                StatusText = $"Switching to {targetCamera.Name}...";
            });

            await _cameraService.StartCaptureAsync(targetCamera.Index).ConfigureAwait(false);

            UpdateOnUiThread(() =>
            {
                IsStreaming = true;
                StatusText = $"Streaming Live ({targetCamera.Name})";
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to switch camera to index {CameraIndex}.", targetCamera.Index);
            UpdateOnUiThread(() =>
            {
                StatusText = $"Camera switch failed: {ex.Message}";
            });
        }
    }

    private bool IsCurrentNavigation(int version, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return version == Volatile.Read(ref _navigationVersion);
    }

    private void OnFrameProcessed(object? sender, LiveFrameProcessedEventArgs e)
    {
        var result = e.Result;
        if (result == null)
        {
            return;
        }

        var dispatcher = GetUiDispatcher();
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            ApplyFrameResult(result);
            return;
        }

        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        // Latest-frame-wins: while a dispatch is already queued, newer frames replace the pending
        // result instead of queueing one UI operation per frame.
        Volatile.Write(ref _pendingFrameResult, result);
        TryScheduleFrameDispatch(dispatcher);
    }

    private void TryScheduleFrameDispatch(System.Windows.Threading.Dispatcher dispatcher)
    {
        if (Interlocked.CompareExchange(ref _frameDispatchScheduled, 1, 0) != 0)
        {
            return;
        }

        try
        {
            dispatcher.InvokeAsync(DispatchPendingFrameResult);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException)
        {
            // Dispatcher shut down between the guard and the post.
            Volatile.Write(ref _pendingFrameResult, null);
            Volatile.Write(ref _frameDispatchScheduled, 0);
        }
    }

    private void DispatchPendingFrameResult()
    {
        try
        {
            var result = Interlocked.Exchange(ref _pendingFrameResult, null);
            if (result != null)
            {
                ApplyFrameResult(result);
            }
        }
        finally
        {
            Volatile.Write(ref _frameDispatchScheduled, 0);

            // A frame may have arrived between the exchange and the flag reset; schedule it.
            if (Volatile.Read(ref _pendingFrameResult) != null)
            {
                var dispatcher = GetUiDispatcher();
                if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                {
                    TryScheduleFrameDispatch(dispatcher);
                }
            }
        }
    }

    private void ApplyFrameResult(LiveFrameResult result)
    {
        CurrentFrameImage = result.AnnotatedImage;
        IsPass = result.IsPass;
        DefectCount = result.DefectCount;
        InferenceLatencyMs = result.Telemetry.InferenceLatencyMs;
        Fps = result.Telemetry.Fps;
        DeviceName = result.Telemetry.DeviceName;
    }

    private System.Windows.Threading.Dispatcher? GetUiDispatcher()
        => UiDispatcherProvider?.Invoke() ?? System.Windows.Application.Current?.Dispatcher;

    private void UpdateOnUiThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = GetUiDispatcher();
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            // Never block a background thread on the UI thread: during shutdown the UI thread
            // disposes the service provider, so a synchronous Invoke would deadlock.
            dispatcher.InvokeAsync(action);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "Skipped a UI update because the dispatcher is shutting down.");
        }
    }
}
