using System;
using System.IO;
using System.Windows.Media.Imaging;
using FluentAssertions;
using OpenCvSharp;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Inference;
using pcb_deftek.Infrastructure.Vision;
using Xunit;

namespace pcb_deftek.Tests;

public class LiveTelemetryTests
{
    private static BitmapSource CreateTestBitmap()
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

    [Fact]
    public void LiveInspectionTelemetry_Empty_ReturnsExpectedDefaults()
    {
        var telemetry = LiveInspectionTelemetry.Empty;

        telemetry.InferenceLatencyMs.Should().Be(0.0);
        telemetry.AverageLatencyMs.Should().Be(0.0);
        telemetry.Fps.Should().Be(0.0);
        telemetry.DeviceName.Should().Be("Unknown");
        telemetry.TimestampUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void LiveInspectionTelemetry_Constructor_InitializesAllFields()
    {
        var timestamp = new DateTime(2026, 11, 13, 15, 30, 0, DateTimeKind.Utc);
        var telemetry = new LiveInspectionTelemetry(14.5, 15.2, 58.4, "DirectML GPU", timestamp);

        telemetry.InferenceLatencyMs.Should().Be(14.5);
        telemetry.AverageLatencyMs.Should().Be(15.2);
        telemetry.Fps.Should().Be(58.4);
        telemetry.DeviceName.Should().Be("DirectML GPU");
        telemetry.TimestampUtc.Should().Be(timestamp);
    }

    [Fact]
    public void LiveFrameResult_WhenCleanBoard_IsPassIsTrueAndDefectCountIsZero()
    {
        var bitmap = CreateTestBitmap();
        using var rawMat = new Mat(100, 100, MatType.CV_8UC3, new Scalar(0, 0, 0));
        var inference = new InferenceResult(
            Guid.NewGuid(),
            Array.Empty<DetectedDefect>(),
            new LetterboxTransform(640, 640, 640, 640, 1.0f, 0, 0),
            ExecutionDevice.GpuDirectML,
            new InferenceTiming(2, 12, 1, 15));
        var telemetry = new LiveInspectionTelemetry(15.0, 15.0, 60.0, "DirectML GPU", DateTime.UtcNow);

        using var frameResult = new LiveFrameResult(bitmap, rawMat.Clone(), inference, telemetry);

        frameResult.AnnotatedImage.Should().BeSameAs(bitmap);
        frameResult.Inference.Should().BeSameAs(inference);
        frameResult.Telemetry.Should().BeSameAs(telemetry);
        frameResult.DefectCount.Should().Be(0);
        frameResult.IsPass.Should().BeTrue();
    }

    [Fact]
    public void LiveFrameResult_WhenDefectDetected_IsPassIsFalseAndCountMatches()
    {
        var bitmap = CreateTestBitmap();
        using var rawMat = new Mat(100, 100, MatType.CV_8UC3, new Scalar(0, 0, 0));
        var defects = new[]
        {
            new DetectedDefect(Guid.NewGuid(), Guid.NewGuid(), "short", 0.95f, new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f))
        };
        var inference = new InferenceResult(
            Guid.NewGuid(),
            defects,
            new LetterboxTransform(640, 640, 640, 640, 1.0f, 0, 0),
            ExecutionDevice.GpuDirectML,
            new InferenceTiming(2, 12, 1, 15));
        var telemetry = new LiveInspectionTelemetry(15.0, 15.0, 60.0, "DirectML GPU", DateTime.UtcNow);

        using var frameResult = new LiveFrameResult(bitmap, rawMat.Clone(), inference, telemetry);

        frameResult.DefectCount.Should().Be(1);
        frameResult.IsPass.Should().BeFalse();
    }

    [Fact]
    public void LiveFrameResult_WhenDisposed_DisposesRawMat()
    {
        var bitmap = CreateTestBitmap();
        var rawMat = new Mat(10, 10, MatType.CV_8UC3, new Scalar(0, 0, 0));
        var inference = new InferenceResult(
            Guid.NewGuid(),
            Array.Empty<DetectedDefect>(),
            new LetterboxTransform(640, 640, 640, 640, 1.0f, 0, 0),
            ExecutionDevice.Cpu,
            new InferenceTiming(2, 12, 1, 15));
        var telemetry = LiveInspectionTelemetry.Empty;

        var frameResult = new LiveFrameResult(bitmap, rawMat, inference, telemetry);
        frameResult.Dispose();

        // Accessing IsDisposed
        rawMat.IsDisposed.Should().BeTrue();
        frameResult.TryCloneRawFrame().Should().BeNull();
    }

    [Fact]
    public void LiveFrameResult_TryCloneRawFrame_WhenActive_ReturnsIndependentClone()
    {
        var bitmap = CreateTestBitmap();
        using var rawMat = new Mat(20, 20, MatType.CV_8UC3, new Scalar(10, 20, 30));
        var inference = new InferenceResult(
            Guid.NewGuid(),
            Array.Empty<DetectedDefect>(),
            new LetterboxTransform(640, 640, 640, 640, 1.0f, 0, 0),
            ExecutionDevice.Cpu,
            new InferenceTiming(2, 12, 1, 15));
        var telemetry = LiveInspectionTelemetry.Empty;

        using var frameResult = new LiveFrameResult(bitmap, rawMat.Clone(), inference, telemetry);
        using var cloned = frameResult.TryCloneRawFrame();

        cloned.Should().NotBeNull();
        cloned!.Width.Should().Be(20);
        cloned.Height.Should().Be(20);
        cloned.Should().NotBeSameAs(frameResult.RawFrame);
    }
}
