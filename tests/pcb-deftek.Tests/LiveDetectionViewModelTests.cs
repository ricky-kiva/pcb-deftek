using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCvSharp;
using pcb_deftek.Domain.Models;
using pcb_deftek.Domain.Models.Messages;
using pcb_deftek.Infrastructure.Inference;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;
using pcb_deftek.Infrastructure.Vision;
using pcb_deftek.Presentation.Navigation;
using pcb_deftek.Presentation.ViewModels;
using Xunit;

namespace pcb_deftek.Tests;

public class LiveDetectionViewModelTests
{
    private readonly Mock<ICameraService> _mockCamera = new();
    private readonly Mock<ILiveInspectionEngine> _mockEngine = new();
    private readonly Mock<ILiveSnapshotService> _mockSnapshot = new();
    private readonly Mock<IUserSettingsService> _mockSettings = new();
    private readonly Mock<INotificationService> _mockNotification = new();
    private readonly WeakReferenceMessenger _messenger = new();
    public LiveDetectionViewModelTests()
    {
        _mockSettings
            .Setup(s => s.LoadSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { ConfidenceThreshold = 0.35 });

        _mockCamera
            .Setup(c => c.GetAvailableCamerasAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CameraDeviceInfo>
            {
                new(0, "USB Microscope Camera"),
                new(1, "Integrated Webcam")
            });

        _mockEngine.SetupProperty(e => e.ConfidenceThreshold, 0.25);
    }

    private LiveDetectionViewModel CreateSut()
    {
        return new LiveDetectionViewModel(
            _mockCamera.Object,
            _mockEngine.Object,
            _mockSnapshot.Object,
            _mockSettings.Object,
            _mockNotification.Object,
            NullLogger<LiveDetectionViewModel>.Instance,
            _messenger);
    }

    private static LiveFrameResult CreateFrameResult(int defectCount)
    {
        var rawMat = new Mat(100, 100, MatType.CV_8UC3, new Scalar(20, 40, 60));
        int stride = (int)rawMat.Step();
        var bitmap = BitmapSource.Create(
            rawMat.Width,
            rawMat.Height,
            96.0,
            96.0,
            PixelFormats.Bgr24,
            null,
            rawMat.Data,
            stride * rawMat.Height,
            stride);
        bitmap.Freeze();

        var defects = new List<DetectedDefect>();
        for (int i = 0; i < defectCount; i++)
        {
            defects.Add(new DetectedDefect(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "short",
                0.91f,
                new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f)));
        }

        var inference = new InferenceResult(
            Guid.NewGuid(),
            defects,
            new LetterboxTransform(640, 640, 100, 100, 6.4f, 0, 0),
            ExecutionDevice.GpuDirectML,
            new InferenceTiming(2, 10, 1, 13));

        var telemetry = new LiveInspectionTelemetry(13.4, 14.1, 59.0, "DirectML GPU", DateTime.UtcNow);
        return new LiveFrameResult(bitmap, rawMat, inference, telemetry);
    }

    [Fact]
    public void InitialState_HasExpectedDefaults()
    {
        var sut = CreateSut();

        sut.Title.Should().Be("Live Detection");
        sut.IsStreaming.Should().BeFalse();
        sut.IsPass.Should().BeTrue();
        sut.StatusBadgeText.Should().Be("PASS");
        sut.DefectCount.Should().Be(0);
        sut.DefectCountText.Should().Be("0 Defects");
        sut.IsTelemetryEnabled.Should().BeTrue();
        sut.ConfidenceThreshold.Should().Be(0.25);
        sut.ConfidencePercentageText.Should().Be("25%");
        sut.CanCaptureSnapshot.Should().BeFalse();
    }

    [Fact]
    public async Task OnNavigatedToAsync_LoadsSettingsEnumeratesCamerasStartsStreamAndCollapsesSidebar()
    {
        var mockNav = new Mock<INavigationService>();
        var mainVm = new MainViewModel(mockNav.Object, messenger: _messenger);
        mainVm.IsSidebarExpanded.Should().BeTrue();

        var sut = CreateSut();

        // Act
        await sut.OnNavigatedToAsync();

        // Assert - Sidebar collapsed via WeakReferenceMessenger
        mainVm.IsSidebarExpanded.Should().BeFalse();

        // Assert - Settings and cameras loaded
        sut.ConfidenceThreshold.Should().Be(0.35);
        _mockEngine.Object.ConfidenceThreshold.Should().Be(0.35);
        sut.AvailableCameras.Should().HaveCount(2);
        sut.SelectedCamera.Should().NotBeNull();
        sut.SelectedCamera!.Index.Should().Be(0);
        sut.IsStreaming.Should().BeTrue();
        sut.CanCaptureSnapshot.Should().BeTrue();

        _mockEngine.Verify(e => e.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockCamera.Verify(c => c.StartCaptureAsync(0, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnNavigatedFromAsync_StopsStreamStopsEngineAndRestoresSidebar()
    {
        var mockNav = new Mock<INavigationService>();
        var mainVm = new MainViewModel(mockNav.Object, messenger: _messenger);
        var sut = CreateSut();

        await sut.OnNavigatedToAsync();
        mainVm.IsSidebarExpanded.Should().BeFalse();

        // Act
        await sut.OnNavigatedFromAsync();

        // Assert
        sut.IsStreaming.Should().BeFalse();
        sut.CanCaptureSnapshot.Should().BeFalse();
        mainVm.IsSidebarExpanded.Should().BeTrue();

        _mockEngine.Verify(e => e.StopAsync(), Times.Once);
        _mockCamera.Verify(c => c.StopCaptureAsync(), Times.Once);
    }

    [Fact]
    public async Task WhenFrameProcessed_UpdatesViewportImageStatusAndTelemetry()
    {
        var sut = CreateSut();
        await sut.OnNavigatedToAsync();

        using var frameResult = CreateFrameResult(defectCount: 2);

        // Act: Raise FrameProcessed
        _mockEngine.Raise(
            e => e.FrameProcessed += null,
            new LiveFrameProcessedEventArgs(frameResult));

        // Assert
        sut.CurrentFrameImage.Should().BeSameAs(frameResult.AnnotatedImage);
        sut.IsPass.Should().BeFalse();
        sut.StatusBadgeText.Should().Be("FAIL");
        sut.DefectCount.Should().Be(2);
        sut.DefectCountText.Should().Be("2 Defects");
        sut.InferenceLatencyMs.Should().Be(13.4);
        sut.Fps.Should().Be(59.0);
        sut.DeviceName.Should().Be("DirectML GPU");
    }

    [Fact]
    public async Task WhenSelectedCameraChangedWhileStreaming_SwitchesCameraCaptureToNewDevice()
    {
        var sut = CreateSut();
        await sut.OnNavigatedToAsync();

        var secondCamera = sut.AvailableCameras[1];

        // Act
        sut.SelectedCamera = secondCamera;

        // Allow async switch to complete
        await Task.Delay(50);

        // Assert
        _mockCamera.Verify(c => c.StartCaptureAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ConfidenceThreshold_WhenChangedOrMessaged_ClampsAndSyncsWithLiveEngine()
    {
        var sut = CreateSut();

        sut.ConfidenceThreshold = 0.65;
        sut.ConfidenceThreshold.Should().Be(0.65);
        sut.ConfidencePercentageText.Should().Be("65%");
        _mockEngine.Object.ConfidenceThreshold.Should().Be(0.65);

        // Broadcast from Settings modal
        _messenger.Send(new ConfidenceThresholdChangedMessage(0.45));
        sut.ConfidenceThreshold.Should().Be(0.45);
        _mockEngine.Object.ConfidenceThreshold.Should().Be(0.45);
    }

    [Fact]
    public void ToggleTelemetryCommand_TogglesIsTelemetryEnabled()
    {
        var sut = CreateSut();
        sut.IsTelemetryEnabled.Should().BeTrue();

        sut.ToggleTelemetryCommand.Execute(null);
        sut.IsTelemetryEnabled.Should().BeFalse();

        sut.ToggleTelemetryCommand.Execute(null);
        sut.IsTelemetryEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task CaptureSnapshotCommand_WhenStreaming_InvokesSnapshotServiceAndSendsToast()
    {
        var sut = CreateSut();
        await sut.OnNavigatedToAsync();

        var capturedRecord = InspectionRecord.Create(
            "2026-11-13-001",
            DetectionMode.LiveCamera,
            @"C:\Captures\snap1_raw.png");

        _mockSnapshot
            .Setup(s => s.CaptureLatestAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(capturedRecord);

        // Act
        await sut.CaptureSnapshotCommand.ExecuteAsync(null);

        // Assert
        sut.LastCapturedRecord.Should().BeSameAs(capturedRecord);
        sut.StatusText.Should().Contain("2026-11-13-001");
        _mockSnapshot.Verify(s => s.CaptureLatestAsync(null, It.IsAny<CancellationToken>()), Times.Once);
        _mockNotification.Verify(
            n => n.ShowToastAsync(
                It.Is<string>(t => t.Contains("Snapshot")),
                It.Is<string>(m => m.Contains("2026-11-13-001")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
