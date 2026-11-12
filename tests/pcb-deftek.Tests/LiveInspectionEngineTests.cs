using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCvSharp;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Inference;
using pcb_deftek.Infrastructure.Services;
using pcb_deftek.Infrastructure.Vision;
using Xunit;

namespace pcb_deftek.Tests;

public class LiveInspectionEngineTests
{
    private readonly Mock<ICameraService> _mockCamera = new();
    private readonly Mock<IPcbDefectDetector> _mockDetector = new();
    private readonly Mock<IImageAnnotationService> _mockAnnotation = new();

    private static BitmapSource CreateFrozenBitmap()
    {
        var bitmap = new BitmapImage();
        using var stream = new MemoryStream();
        using var mat = new Mat(10, 10, MatType.CV_8UC3, new Scalar(0, 0, 0));
        byte[] bytes = mat.ToBytes(".bmp");
        stream.Write(bytes, 0, bytes.Length);
        stream.Position = 0;

        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();

        return bitmap;
    }

    private static InferenceResult CreateInferenceResult(int defectCount = 0)
    {
        var defects = new List<DetectedDefect>();
        for (int i = 0; i < defectCount; i++)
        {
            defects.Add(new DetectedDefect(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "short",
                0.90f,
                new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f)));
        }

        return new InferenceResult(
            Guid.NewGuid(),
            defects,
            new LetterboxTransform(640, 640, 640, 640, 1.0f, 0, 0),
            ExecutionDevice.GpuDirectML,
            new InferenceTiming(2, 10, 1, 13));
    }

    [Fact]
    public async Task StartAsync_WhenInvoked_SetsIsRunningToTrueAndSubscribesToCamera()
    {
        var sut = new LiveInspectionEngine(
            _mockCamera.Object,
            _mockDetector.Object,
            _mockAnnotation.Object,
            NullLogger<LiveInspectionEngine>.Instance);

        sut.IsRunning.Should().BeFalse();

        await sut.StartAsync();

        sut.IsRunning.Should().BeTrue();

        await sut.StopAsync();

        sut.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void ConfidenceThreshold_ClampsWithinValidRange()
    {
        var sut = new LiveInspectionEngine(
            _mockCamera.Object,
            _mockDetector.Object,
            _mockAnnotation.Object,
            NullLogger<LiveInspectionEngine>.Instance);

        sut.ConfidenceThreshold = 0.50;
        sut.ConfidenceThreshold.Should().Be(0.50);

        sut.ConfidenceThreshold = 0.05;
        sut.ConfidenceThreshold.Should().Be(0.10);

        sut.ConfidenceThreshold = 1.50;
        sut.ConfidenceThreshold.Should().Be(0.95);
    }

    [Fact]
    public async Task WhenFrameReceived_ProcessesFrameAndRaisesFrameProcessed()
    {
        var frozenBitmap = CreateFrozenBitmap();
        var inference = CreateInferenceResult(defectCount: 0);

        _mockDetector
            .Setup(d => d.DetectAsync(
                It.IsAny<Mat>(),
                It.IsAny<Guid?>(),
                It.IsAny<float?>(),
                It.IsAny<float?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(inference);

        _mockAnnotation
            .Setup(a => a.AnnotateMat(It.IsAny<Mat>(), It.IsAny<IReadOnlyList<DetectedDefect>>(), It.IsAny<int>()))
            .Returns(frozenBitmap);

        var sut = new LiveInspectionEngine(
            _mockCamera.Object,
            _mockDetector.Object,
            _mockAnnotation.Object,
            NullLogger<LiveInspectionEngine>.Instance);

        var processedTcs = new TaskCompletionSource<LiveFrameResult>();
        sut.FrameProcessed += (s, e) => processedTcs.TrySetResult(e.Result);

        await sut.StartAsync();

        // Simulate camera frame delivery
        using var incomingMat = new Mat(100, 100, MatType.CV_8UC3, new Scalar(50, 50, 50));
        _mockCamera.Raise(c => c.FrameReceived += null, new CameraFrameEventArgs(incomingMat));

        var completedTask = await Task.WhenAny(processedTcs.Task, Task.Delay(2000));
        completedTask.Should().Be(processedTcs.Task);

        var result = await processedTcs.Task;
        result.Should().NotBeNull();
        result.AnnotatedImage.Should().BeSameAs(frozenBitmap);
        result.Inference.Should().BeSameAs(inference);
        result.IsPass.Should().BeTrue();
        result.DefectCount.Should().Be(0);
        result.Telemetry.InferenceLatencyMs.Should().BeGreaterThanOrEqualTo(0);

        await sut.StopAsync();
    }

    [Fact]
    public async Task WhenMultipleFramesArriveQuickly_DropsStaleFramesToPreventBacklog()
    {
        var frozenBitmap = CreateFrozenBitmap();
        var inference = CreateInferenceResult(defectCount: 1);

        var gate = new TaskCompletionSource<bool>();
        var inferenceCount = 0;

        _mockDetector
            .Setup(d => d.DetectAsync(
                It.IsAny<Mat>(),
                It.IsAny<Guid?>(),
                It.IsAny<float?>(),
                It.IsAny<float?>(),
                It.IsAny<CancellationToken>()))
            .Returns<Mat, Guid?, float?, float?, CancellationToken>(async (mat, id, conf, iou, token) =>
            {
                Interlocked.Increment(ref inferenceCount);
                await gate.Task; // Hold inference to let pending frames build up
                return inference;
            });

        _mockAnnotation
            .Setup(a => a.AnnotateMat(It.IsAny<Mat>(), It.IsAny<IReadOnlyList<DetectedDefect>>(), It.IsAny<int>()))
            .Returns(frozenBitmap);

        var sut = new LiveInspectionEngine(
            _mockCamera.Object,
            _mockDetector.Object,
            _mockAnnotation.Object,
            NullLogger<LiveInspectionEngine>.Instance);

        var processedCount = 0;
        sut.FrameProcessed += (s, e) => Interlocked.Increment(ref processedCount);

        await sut.StartAsync();

        using var frame1 = new Mat(50, 50, MatType.CV_8UC3, new Scalar(1, 1, 1));
        using var frame2 = new Mat(50, 50, MatType.CV_8UC3, new Scalar(2, 2, 2));
        using var frame3 = new Mat(50, 50, MatType.CV_8UC3, new Scalar(3, 3, 3));
        using var frame4 = new Mat(50, 50, MatType.CV_8UC3, new Scalar(4, 4, 4));

        // Send 4 frames rapidly
        _mockCamera.Raise(c => c.FrameReceived += null, new CameraFrameEventArgs(frame1));
        _mockCamera.Raise(c => c.FrameReceived += null, new CameraFrameEventArgs(frame2));
        _mockCamera.Raise(c => c.FrameReceived += null, new CameraFrameEventArgs(frame3));
        _mockCamera.Raise(c => c.FrameReceived += null, new CameraFrameEventArgs(frame4));

        // Release the held inference
        gate.SetResult(true);

        // Allow loop to finish pending frames
        await Task.Delay(250);

        await sut.StopAsync();

        // Out of 4 rapid frames, frame 1 was processed, and at most frame 4 (stale frame 2 and 3 dropped)
        inferenceCount.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task WhenDefectiveFrameProcessed_SetsFailStatusAndUpdatesRollingFpsAndDeviceName()
    {
        var frozenBitmap = CreateFrozenBitmap();
        var defectiveInference = CreateInferenceResult(defectCount: 3);

        _mockDetector
            .Setup(d => d.DetectAsync(
                It.IsAny<Mat>(),
                It.IsAny<Guid?>(),
                It.IsAny<float?>(),
                It.IsAny<float?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(defectiveInference);

        _mockAnnotation
            .Setup(a => a.AnnotateMat(It.IsAny<Mat>(), It.IsAny<IReadOnlyList<DetectedDefect>>(), It.IsAny<int>()))
            .Returns(frozenBitmap);

        await using var sut = new LiveInspectionEngine(
            _mockCamera.Object,
            _mockDetector.Object,
            _mockAnnotation.Object,
            NullLogger<LiveInspectionEngine>.Instance);

        var frame1Tcs = new TaskCompletionSource<LiveFrameResult>();
        var frame2Tcs = new TaskCompletionSource<LiveFrameResult>();
        var count = 0;

        sut.FrameProcessed += (s, e) =>
        {
            int idx = Interlocked.Increment(ref count);
            if (idx == 1)
            {
                frame1Tcs.TrySetResult(e.Result);
            }
            else if (idx == 2)
            {
                frame2Tcs.TrySetResult(e.Result);
            }
        };

        await sut.StartAsync();

        using var mat = new Mat(100, 100, MatType.CV_8UC3, new Scalar(0, 0, 0));
        _mockCamera.Raise(c => c.FrameReceived += null, new CameraFrameEventArgs(mat));
        var res1 = await frame1Tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        _mockCamera.Raise(c => c.FrameReceived += null, new CameraFrameEventArgs(mat));
        var res2 = await frame2Tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        res1.IsPass.Should().BeFalse();
        res1.DefectCount.Should().Be(3);
        res2.Telemetry.Fps.Should().Be(2.0);
        res2.Telemetry.DeviceName.Should().Be("DirectML GPU");
        sut.LatestResult.Should().BeSameAs(res2);
        sut.CurrentTelemetry.Fps.Should().Be(2.0);
    }
}
