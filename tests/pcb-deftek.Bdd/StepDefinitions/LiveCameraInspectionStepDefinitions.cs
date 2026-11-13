using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Inference;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence;
using pcb_deftek.Infrastructure.Persistence.Migrations;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;
using pcb_deftek.Infrastructure.Vision;
using pcb_deftek.Presentation.ViewModels;
using Reqnroll;

namespace pcb_deftek.Bdd.StepDefinitions;

[Binding]
public class LiveCameraInspectionStepDefinitions : IDisposable
{
    private readonly string _testRoot;
    private readonly string _dbPath;
    private readonly string _capturesDir;
    private readonly FakeCameraService _camera = new();
    private readonly StubPcbDefectDetector _detector = new();
    private readonly RecordingAnnotationService _annotation = new();
    private readonly CapturingNotificationService _notifications = new();

    private ServiceProvider? _serviceProvider;
    private LiveDetectionViewModel? _viewModel;
    private ILiveInspectionEngine? _engine;
    private IInspectionRepository? _repository;

    public LiveCameraInspectionStepDefinitions()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "pcb-deftek-bdd-live-" + Guid.NewGuid().ToString("N"));
        _dbPath = Path.Combine(_testRoot, "app.db");
        _capturesDir = Path.Combine(_testRoot, "Captures");
        Directory.CreateDirectory(_testRoot);
    }

    [Given(@"a live camera inspection environment with two connected cameras and a test SQLite database")]
    public async Task GivenALiveCameraInspectionEnvironmentWithTwoConnectedCamerasAndATestSqliteDatabase()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);

        services.AddSingleton<ISqliteConnectionFactory>(_ => new TestConnectionFactory(_dbPath));
        services.AddSingleton<ICameraService>(_camera);
        services.AddSingleton<IPcbDefectDetector>(_detector);
        services.AddSingleton<IImageAnnotationService>(_annotation);
        services.AddSingleton<INotificationService>(_notifications);
        services.AddSingleton<ILiveSnapshotService>(sp => new LiveSnapshotService(
            sp.GetRequiredService<IInspectionRepository>(),
            sp.GetRequiredService<ILiveInspectionEngine>(),
            _capturesDir));

        _serviceProvider = services.BuildServiceProvider();

        var migrator = _serviceProvider.GetRequiredService<IDatabaseMigrator>();
        await migrator.MigrateToLatestAsync();

        _repository = _serviceProvider.GetRequiredService<IInspectionRepository>();
        _engine = _serviceProvider.GetRequiredService<ILiveInspectionEngine>();
        _viewModel = _serviceProvider.GetRequiredService<LiveDetectionViewModel>();
    }

    [When(@"the operator opens the Live Detection page")]
    public async Task WhenTheOperatorOpensTheLiveDetectionPage()
    {
        _viewModel.Should().NotBeNull();
        await _viewModel!.OnNavigatedToAsync();

        _viewModel.IsStreaming.Should().BeTrue();
        _viewModel.AvailableCameras.Should().HaveCount(2);
    }

    [When(@"the live camera delivers a clean frame with no defects")]
    public Task WhenTheLiveCameraDeliversACleanFrameWithNoDefects()
    {
        return DeliverFrameAsync(defectCount: 0);
    }

    [When(@"the live camera delivers a frame containing (.*) defects?")]
    public Task WhenTheLiveCameraDeliversAFrameContainingDefects(int defectCount)
    {
        return DeliverFrameAsync(defectCount);
    }

    [Then(@"the viewport should display the annotated camera frame")]
    public void ThenTheViewportShouldDisplayTheAnnotatedCameraFrame()
    {
        _viewModel!.CurrentFrameImage.Should().NotBeNull();
        _viewModel.CurrentFrameImage.Should().BeSameAs(_annotation.LastAnnotatedImage);
    }

    [Then(@"the inspection status badge should read ""(.*)""")]
    public void ThenTheInspectionStatusBadgeShouldRead(string expectedStatus)
    {
        _viewModel!.StatusBadgeText.Should().Be(expectedStatus);
    }

    [Then(@"the defect counter should read ""(.*)""")]
    public void ThenTheDefectCounterShouldRead(string expectedCounter)
    {
        _viewModel!.DefectCountText.Should().Be(expectedCounter);
    }

    [Then(@"the annotation service should have rendered (.*) defect overlays")]
    public void ThenTheAnnotationServiceShouldHaveRenderedDefectOverlays(int expectedCount)
    {
        _annotation.LastDefects.Should().NotBeNull();
        _annotation.LastDefects!.Should().HaveCount(expectedCount);
    }

    [Then(@"the telemetry readout should report a non-negative inference latency and a positive frame rate")]
    public void ThenTheTelemetryReadoutShouldReportLatencyAndFrameRate()
    {
        _viewModel!.InferenceLatencyMs.Should().BeGreaterThanOrEqualTo(0.0);
        _viewModel.Fps.Should().BeGreaterThan(0.0);
        _viewModel.DeviceName.Should().NotBeNullOrWhiteSpace();
    }

    [Then(@"the telemetry card should be visible")]
    public void ThenTheTelemetryCardShouldBeVisible()
    {
        _viewModel!.IsTelemetryEnabled.Should().BeTrue();
        _viewModel.TelemetryCardOpacity.Should().Be(1.0);
    }

    [Then(@"the telemetry card should be hidden")]
    public void ThenTheTelemetryCardShouldBeHidden()
    {
        _viewModel!.IsTelemetryEnabled.Should().BeFalse();
        _viewModel.TelemetryCardOpacity.Should().Be(0.0);
    }

    [When(@"the operator disables telemetry")]
    public void WhenTheOperatorDisablesTelemetry()
    {
        _viewModel!.ToggleTelemetryCommand.Execute(null);
    }

    [When(@"the operator enables telemetry")]
    public void WhenTheOperatorEnablesTelemetry()
    {
        _viewModel!.ToggleTelemetryCommand.Execute(null);
    }

    [When(@"the cursor remains stationary over the viewport for three seconds")]
    public void WhenTheCursorRemainsStationaryOverTheViewportForThreeSeconds()
    {
        _viewModel!.TriggerIdleAutoHide();
    }

    [Then(@"the configuration controls should be hidden")]
    public void ThenTheConfigurationControlsShouldBeHidden()
    {
        _viewModel!.ConfigurationControlsOpacity.Should().Be(0.0);
    }

    [Then(@"the inspection status card should remain visible")]
    public void ThenTheInspectionStatusCardShouldRemainVisible()
    {
        _viewModel!.StatusCardOpacity.Should().Be(1.0);
        _viewModel.StatusBadgeText.Should().NotBeNullOrWhiteSpace();
    }

    [Then(@"the telemetry card should remain visible")]
    public void ThenTheTelemetryCardShouldRemainVisible()
    {
        _viewModel!.TelemetryCardOpacity.Should().Be(1.0);
    }

    [When(@"the operator moves the mouse")]
    public void WhenTheOperatorMovesTheMouse()
    {
        _viewModel!.NotifyUserActivity();
    }

    [Then(@"all floating controls should be visible again")]
    public void ThenAllFloatingControlsShouldBeVisibleAgain()
    {
        _viewModel!.ConfigurationControlsOpacity.Should().Be(1.0);
        _viewModel.StatusCardOpacity.Should().Be(1.0);
        _viewModel.TelemetryCardOpacity.Should().Be(1.0);
    }

    [When(@"the operator presses Space to capture the frame")]
    public async Task WhenTheOperatorPressesSpaceToCaptureTheFrame()
    {
        _viewModel!.CanCaptureSnapshot.Should().BeTrue();
        await _viewModel.CaptureSnapshotCommand.ExecuteAsync(null);
    }

    [Then(@"a live camera inspection record with (.*) defects should be persisted in the database")]
    public async Task ThenALiveCameraInspectionRecordWithDefectsShouldBePersisted(int expectedDefects)
    {
        _repository.Should().NotBeNull();
        _viewModel!.LastCapturedRecord.Should().NotBeNull();

        var record = _viewModel.LastCapturedRecord!;
        record.Mode.Should().Be(DetectionMode.LiveCamera);
        record.DefectCount.Should().Be(expectedDefects);

        var persisted = await _repository!.GetByIdAsync(record.Id);
        persisted.Should().NotBeNull();
        persisted!.Mode.Should().Be(DetectionMode.LiveCamera);
        persisted.Defects.Should().HaveCount(expectedDefects);
    }

    [Then(@"the raw and annotated snapshot images should exist on disk")]
    public void ThenTheRawAndAnnotatedSnapshotImagesShouldExistOnDisk()
    {
        var record = _viewModel!.LastCapturedRecord!;

        File.Exists(record.ImagePath).Should().BeTrue();
        new FileInfo(record.ImagePath).Length.Should().BeGreaterThan(0);

        var snapshotService = _serviceProvider!.GetRequiredService<ILiveSnapshotService>();
        string annotatedPath = snapshotService.GetAnnotatedImagePath(record.ImagePath);
        File.Exists(annotatedPath).Should().BeTrue();
        new FileInfo(annotatedPath).Length.Should().BeGreaterThan(0);
    }

    [Then(@"a capture toast notification should be dispatched")]
    public void ThenACaptureToastNotificationShouldBeDispatched()
    {
        _notifications.Toasts.Should().ContainSingle(
            toast => toast.Title.Contains("Snapshot", StringComparison.OrdinalIgnoreCase));
    }

    [Then(@"camera index (.*) should be actively capturing")]
    public void ThenCameraIndexShouldBeActivelyCapturing(int expectedIndex)
    {
        _camera.IsCapturing.Should().BeTrue();
        _camera.CurrentCamera.Should().NotBeNull();
        _camera.CurrentCamera!.Index.Should().Be(expectedIndex);
    }

    [When(@"the operator selects the second camera source")]
    public async Task WhenTheOperatorSelectsTheSecondCameraSource()
    {
        var secondCamera = _viewModel!.AvailableCameras.SingleOrDefault(c => c.Index == 1);
        secondCamera.Should().NotBeNull();

        _viewModel.SelectedCamera = secondCamera;

        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (_camera.CurrentCamera?.Index != 1 && DateTime.UtcNow < timeout)
        {
            await Task.Delay(20);
        }
    }

    [Then(@"the status should report streaming from ""(.*)""")]
    public void ThenTheStatusShouldReportStreamingFrom(string cameraName)
    {
        _viewModel!.StatusText.Should().Contain("Streaming Live").And.Contain(cameraName);
    }

    private async Task DeliverFrameAsync(int defectCount)
    {
        _viewModel.Should().NotBeNull();
        _engine.Should().NotBeNull();

        _detector.DefectCount = defectCount;

        var processed = new TaskCompletionSource<LiveFrameResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<LiveFrameProcessedEventArgs> handler = (_, args) => processed.TrySetResult(args.Result);
        _engine!.FrameProcessed += handler;

        try
        {
            using var frame = new Mat(120, 160, MatType.CV_8UC3, new Scalar(30, 60, 90));
            _camera.EmitFrame(frame);

            var completed = await Task.WhenAny(processed.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            completed.Should().BeSameAs(processed.Task, "the live inspection engine should process the delivered camera frame");

            await processed.Task;
        }
        finally
        {
            _engine!.FrameProcessed -= handler;
        }
    }

    public void Dispose()
    {
        try
        {
            _viewModel?.OnNavigatedFromAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Best effort shutdown
        }

        _serviceProvider?.Dispose();

        try
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private sealed class TestConnectionFactory : ISqliteConnectionFactory
    {
        public string DatabasePath { get; }

        public TestConnectionFactory(string path) => DatabasePath = path;

        public SqliteConnection CreateConnection() => new($"Data Source={DatabasePath}");

        public async ValueTask<SqliteConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
    }

    private sealed class FakeCameraService : ICameraService
    {
        private readonly IReadOnlyList<CameraDeviceInfo> _cameras = new[]
        {
            new CameraDeviceInfo(0, "USB Inspection Camera"),
            new CameraDeviceInfo(1, "Integrated Webcam")
        };

        public bool IsCapturing { get; private set; }

        public CameraDeviceInfo? CurrentCamera { get; private set; }

        public event EventHandler<CameraFrameEventArgs>? FrameReceived;

        public Task<IReadOnlyList<CameraDeviceInfo>> GetAvailableCamerasAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_cameras);
        }

        public Task StartCaptureAsync(int cameraIndex = 0, CancellationToken cancellationToken = default)
        {
            IsCapturing = true;
            CurrentCamera = _cameras.FirstOrDefault(camera => camera.Index == cameraIndex)
                ?? new CameraDeviceInfo(cameraIndex, $"Camera {cameraIndex}");
            return Task.CompletedTask;
        }

        public Task StopCaptureAsync()
        {
            IsCapturing = false;
            CurrentCamera = null;
            return Task.CompletedTask;
        }

        public void EmitFrame(Mat frame)
        {
            FrameReceived?.Invoke(this, new CameraFrameEventArgs(frame));
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubPcbDefectDetector : IPcbDefectDetector
    {
        public int DefectCount { get; set; }

        private InferenceResult BuildResult()
        {
            var defects = new List<DetectedDefect>(DefectCount);
            for (int i = 0; i < DefectCount; i++)
            {
                defects.Add(new DetectedDefect(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "short",
                    0.92f,
                    new BoundingBox(0.1f, 0.15f, 0.2f, 0.25f)));
            }

            return new InferenceResult(
                Guid.NewGuid(),
                defects,
                new LetterboxTransform(640, 640, 160, 120, 4.0f, 0, 80),
                ExecutionDevice.GpuDirectML,
                new InferenceTiming(1, 6, 1, 8));
        }

        public InferenceResult Detect(Mat sourceMat, Guid? inspectionId = null, float? confidenceThreshold = null, float? nmsIouThreshold = null)
            => BuildResult();

        public Task<InferenceResult> DetectAsync(Mat sourceMat, Guid? inspectionId = null, float? confidenceThreshold = null, float? nmsIouThreshold = null, CancellationToken cancellationToken = default)
            => Task.FromResult(BuildResult());

        public InferenceResult DetectFromFile(string filePath, Guid? inspectionId = null, float? confidenceThreshold = null, float? nmsIouThreshold = null)
            => BuildResult();

        public Task<InferenceResult> DetectFromFileAsync(string filePath, Guid? inspectionId = null, float? confidenceThreshold = null, float? nmsIouThreshold = null, CancellationToken cancellationToken = default)
            => Task.FromResult(BuildResult());

        public InferenceResult DetectFromBytes(ReadOnlySpan<byte> imageBytes, Guid? inspectionId = null, float? confidenceThreshold = null, float? nmsIouThreshold = null)
            => BuildResult();

        public Task<InferenceResult> DetectFromBytesAsync(byte[] imageBytes, Guid? inspectionId = null, float? confidenceThreshold = null, float? nmsIouThreshold = null, CancellationToken cancellationToken = default)
            => Task.FromResult(BuildResult());
    }

    private sealed class RecordingAnnotationService : IImageAnnotationService
    {
        public IReadOnlyList<DetectedDefect>? LastDefects { get; private set; }

        public BitmapSource? LastAnnotatedImage { get; private set; }

        public BitmapSource? AnnotateImage(string imagePath, IReadOnlyList<DetectedDefect> defects, int maxDimension = 0)
        {
            LastDefects = defects;
            return null;
        }

        public BitmapSource? AnnotateMat(Mat sourceMat, IReadOnlyList<DetectedDefect> defects, int maxDimension = 0)
        {
            LastDefects = defects;
            LastAnnotatedImage = CreateFrozenBitmap(sourceMat.Width, sourceMat.Height);
            return LastAnnotatedImage;
        }

        public BitmapSource? MatToBitmapSource(Mat sourceMat)
        {
            LastAnnotatedImage = CreateFrozenBitmap(sourceMat.Width, sourceMat.Height);
            return LastAnnotatedImage;
        }

        private static BitmapSource CreateFrozenBitmap(int width, int height)
        {
            using var mat = new Mat(height, width, MatType.CV_8UC3, new Scalar(40, 80, 120));
            byte[] bytes = mat.ToBytes(".bmp");
            using var stream = new MemoryStream(bytes);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
    }

    private sealed class CapturingNotificationService : INotificationService
    {
        private readonly List<(string Title, string Message)> _toasts = new();

        public IReadOnlyList<(string Title, string Message)> Toasts
        {
            get
            {
                lock (_toasts)
                {
                    return _toasts.ToList();
                }
            }
        }

        public Task ShowToastAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            lock (_toasts)
            {
                _toasts.Add((title, message));
            }

            return Task.CompletedTask;
        }
    }
}
