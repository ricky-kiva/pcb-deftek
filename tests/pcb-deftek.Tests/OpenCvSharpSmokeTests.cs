using System;
using System.IO;
using FluentAssertions;
using OpenCvSharp;
using Xunit;

namespace pcb_deftek.Tests;

public class OpenCvSharpSmokeTests
{
    private static string ResolveTestImagePath(string filename)
    {
        string[] candidates =
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "Model", filename),
            Path.Combine(Directory.GetCurrentDirectory(), "tests", "pcb-deftek.Tests", "TestData", "Model", filename),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "TestData", "Model", filename)
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                return Path.GetFullPath(c);
            }
        }

        throw new FileNotFoundException($"Test image fixture '{filename}' not found.");
    }

    [Fact]
    public void OpenCvRuntime_WhenLoaded_ReportsValidVersion()
    {
        // Act
        string? version = Cv2.GetVersionString();

        // Assert
        version.Should().NotBeNullOrWhiteSpace();
        version.Should().StartWith("4.");
    }

    [Fact]
    public void OpenCvRuntime_WhenReadingTestImage_DecodesValidDimensionsAndChannels()
    {
        // Arrange
        string imagePath = ResolveTestImagePath("OP-1.jpg");

        // Act
        using var mat = Cv2.ImRead(imagePath, ImreadModes.Color);

        // Assert
        mat.Empty().Should().BeFalse();
        mat.Width.Should().BeGreaterThan(0);
        mat.Height.Should().BeGreaterThan(0);
        mat.Channels().Should().Be(3);
    }

    [Fact]
    public void OpenCvRuntime_WhenResizingImage_ConformsToTargetDimensions()
    {
        // Arrange
        string imagePath = ResolveTestImagePath("OP-1.jpg");
        using var src = Cv2.ImRead(imagePath, ImreadModes.Color);

        // Act
        using var dst = new Mat();
        Cv2.Resize(src, dst, new Size(640, 640), interpolation: InterpolationFlags.Linear);

        // Assert
        dst.Width.Should().Be(640);
        dst.Height.Should().Be(640);
        dst.Channels().Should().Be(3);
    }
}
