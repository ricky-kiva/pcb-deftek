using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using OpenCvSharp;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Services;
using Xunit;

namespace pcb_deftek.Tests;

public class CameraServiceTests
{
    [Fact]
    public void CameraDeviceInfo_Properties_SetCorrectly()
    {
        var device = new CameraDeviceInfo(0, "Integrated Camera", "device-id-123");

        device.Index.Should().Be(0);
        device.Name.Should().Be("Integrated Camera");
        device.DeviceId.Should().Be("device-id-123");
        device.ToString().Should().Be("Integrated Camera");
    }

    [Fact]
    public void CameraFrameEventArgs_WhenFrameProvided_ExposesFrameAndTimestamp()
    {
        using var mat = new Mat(100, 100, MatType.CV_8UC3, new Scalar(0, 0, 0));
        var timestamp = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

        var args = new CameraFrameEventArgs(mat, timestamp);

        args.Frame.Should().BeSameAs(mat);
        args.TimestampUtc.Should().Be(timestamp);
    }

    [Fact]
    public void CameraFrameEventArgs_WhenFrameIsNull_ThrowsArgumentNullException()
    {
        Action act = () => _ = new CameraFrameEventArgs(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task OpenCvCameraService_GetAvailableCamerasAsync_UsesConfiguredEnumerator()
    {
        var expectedCameras = new List<CameraDeviceInfo>
        {
            new(0, "Camera 0"),
            new(1, "Camera 1")
        };

        using var sut = new OpenCvCameraService(
            deviceEnumerator: () => expectedCameras);

        var result = await sut.GetAvailableCamerasAsync();

        result.Should().BeEquivalentTo(expectedCameras);
    }

    [Fact]
    public async Task OpenCvCameraService_StartCaptureAsync_WhenInvokedWithValidDevice_UpdatesStateAndStreamsFrames()
    {
        var cameras = new List<CameraDeviceInfo> { new(0, "Mock Camera") };
        using var testMat = new Mat(50, 50, MatType.CV_8UC3, new Scalar(128, 128, 128));
        var frameTcs = new TaskCompletionSource<Mat>();

        using var sut = new OpenCvCameraService(
            deviceEnumerator: () => cameras,
            frameSource: (index, token) =>
            {
                return Task.FromResult<Mat?>(testMat.Clone());
            });

        sut.FrameReceived += (sender, e) =>
        {
            frameTcs.TrySetResult(e.Frame);
        };

        // Act: Start capture
        await sut.StartCaptureAsync(0);

        sut.IsCapturing.Should().BeTrue();
        sut.CurrentCamera.Should().NotBeNull();
        sut.CurrentCamera!.Index.Should().Be(0);
        sut.CurrentCamera.Name.Should().Be("Mock Camera");

        // Wait for at least one frame
        var receivedFrame = await Task.WhenAny(frameTcs.Task, Task.Delay(1500));
        receivedFrame.Should().Be(frameTcs.Task);

        var frame = await frameTcs.Task;
        frame.Should().NotBeNull();
        frame.Width.Should().Be(50);
        frame.Height.Should().Be(50);

        // Act: Stop capture
        await sut.StopCaptureAsync();

        sut.IsCapturing.Should().BeFalse();
        sut.CurrentCamera.Should().BeNull();
    }

    [Fact]
    public async Task OpenCvCameraService_StartCaptureAsync_WhenInvalidIndex_ThrowsCameraCaptureException()
    {
        var cameras = new List<CameraDeviceInfo> { new(0, "Camera 0") };

        using var sut = new OpenCvCameraService(
            deviceEnumerator: () => cameras);

        Func<Task> act = async () => await sut.StartCaptureAsync(99);

        await act.Should().ThrowAsync<CameraCaptureException>()
            .WithMessage("*99*");
    }

    [Fact]
    public async Task OpenCvCameraService_StopCaptureAsync_WhenIdle_DoesNotThrow()
    {
        using var sut = new OpenCvCameraService(deviceEnumerator: () => Array.Empty<CameraDeviceInfo>());

        sut.IsCapturing.Should().BeFalse();
        var act = async () => await sut.StopCaptureAsync();

        await act.Should().NotThrowAsync();
    }
}
