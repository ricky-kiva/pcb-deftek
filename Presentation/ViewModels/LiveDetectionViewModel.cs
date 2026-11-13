using System;
using System.Collections.ObjectModel;
using System.Linq;
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
    private double _confidenceThreshold = 0.25;

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
        // 1. Signal shell window to collapse sidebar by default on Live Camera page
        _messenger.Send(new SidebarCollapseMessage(true));

        // 2. Load persisted confidence threshold
        try
        {
            var settings = await _userSettingsService.LoadSettingsAsync().ConfigureAwait(false);
            if (settings != null)
            {
                ConfidenceThreshold = settings.ConfidenceThreshold;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load user settings into LiveDetectionViewModel.");
        }

        // 3. Subscribe to live processed frames
        _liveInspectionEngine.FrameProcessed -= OnFrameProcessed;
        _liveInspectionEngine.FrameProcessed += OnFrameProcessed;

        // 4. Discover cameras and start live stream
        try
        {
            var cameras = await _cameraService.GetAvailableCamerasAsync().ConfigureAwait(false);

            UpdateOnUiThread(() =>
            {
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

            int targetIndex = SelectedCamera?.Index ?? 0;
            await _liveInspectionEngine.StartAsync().ConfigureAwait(false);
            await _cameraService.StartCaptureAsync(targetIndex).ConfigureAwait(false);

            UpdateOnUiThread(() =>
            {
                IsStreaming = true;
                StatusText = $"Streaming Live ({SelectedCamera?.Name ?? "Camera 0"})";
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to start live camera capture.");
            UpdateOnUiThread(() =>
            {
                IsStreaming = false;
                StatusText = $"Camera unavailable: {ex.Message}";
            });
        }
    }

    /// <inheritdoc />
    public override async Task OnNavigatedFromAsync()
    {
        // Restore sidebar to expanded when leaving Live Detection page
        _messenger.Send(new SidebarCollapseMessage(false));

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

    private void OnFrameProcessed(object? sender, LiveFrameProcessedEventArgs e)
    {
        var result = e.Result;
        if (result == null)
        {
            return;
        }

        UpdateOnUiThread(() =>
        {
            CurrentFrameImage = result.AnnotatedImage;
            IsPass = result.IsPass;
            DefectCount = result.DefectCount;
            InferenceLatencyMs = result.Telemetry.InferenceLatencyMs;
            Fps = result.Telemetry.Fps;
            DeviceName = result.Telemetry.DeviceName;
        });
    }

    private static void UpdateOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
            return;
        }

        action();
    }
}
