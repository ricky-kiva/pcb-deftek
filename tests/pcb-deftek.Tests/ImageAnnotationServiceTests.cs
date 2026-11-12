using System;
using System.IO;
using FluentAssertions;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Vision;
using Xunit;

namespace pcb_deftek.Tests;

public class ImageAnnotationServiceTests
{
    private readonly ImageAnnotationService _sut = new();

    private static string ResolveTestImagePath(string filename)
    {
        string? current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, "tests", "pcb-deftek.Tests", "TestData", "Model", filename);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            string directCandidate = Path.Combine(current, "TestData", "Model", filename);
            if (File.Exists(directCandidate))
            {
                return Path.GetFullPath(directCandidate);
            }

            var parent = Directory.GetParent(current);
            current = parent?.FullName;
        }

        throw new FileNotFoundException($"Test image '{filename}' not found for testing.");
    }

    [Fact]
    public void AnnotateImage_WhenFileNotFound_ReturnsNull()
    {
        var result = _sut.AnnotateImage(@"C:\pcb\does-not-exist.jpg", Array.Empty<DetectedDefect>());
        result.Should().BeNull();
    }

    [Fact]
    public void AnnotateImage_WithCleanBoard_ReturnsFrozenBitmap()
    {
        string path = ResolveTestImagePath("SH-1.jpg");
        var result = _sut.AnnotateImage(path, Array.Empty<DetectedDefect>());

        result.Should().NotBeNull();
        result!.IsFrozen.Should().BeTrue();
        result.PixelWidth.Should().BeGreaterThan(0);
        result.PixelHeight.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AnnotateImage_WithDefects_RendersAnnotatedImage()
    {
        string path = ResolveTestImagePath("SH-1.jpg");
        var defect = new DetectedDefect(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "short",
            0.92f,
            new BoundingBox(0.2f, 0.2f, 0.3f, 0.3f));

        var result = _sut.AnnotateImage(path, new[] { defect });

        result.Should().NotBeNull();
        result!.IsFrozen.Should().BeTrue();
    }

    [Fact]
    public void AnnotateImage_WithMaxDimension_ScalesOutputAppropriately()
    {
        string path = ResolveTestImagePath("SH-1.jpg");
        var defect = new DetectedDefect(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "short",
            0.88f,
            new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f));

        var result = _sut.AnnotateImage(path, new[] { defect }, maxDimension: 64);

        result.Should().NotBeNull();
        Math.Max(result!.PixelWidth, result.PixelHeight).Should().BeLessThanOrEqualTo(64);
    }

    [Theory]
    [InlineData(DefectClass.Short, 255, 102, 0)]          // Blue
    [InlineData(DefectClass.Spur, 220, 220, 0)]           // Cyan
    [InlineData(DefectClass.SpuriousCopper, 255, 255, 255)]// White
    [InlineData(DefectClass.Open, 80, 160, 79)]           // Fern
    [InlineData(DefectClass.MouseBite, 140, 48, 0)]       // Dark Blue
    [InlineData(DefectClass.HoleBreakout, 180, 105, 255)] // Pink
    [InlineData(DefectClass.ConductorScratch, 35, 17, 232)]// Red
    [InlineData(DefectClass.ConductorForeignObject, 0, 215, 255)]// Yellow
    [InlineData(DefectClass.BaseMaterialForeignObject, 113, 204, 46)]// Green
    public void GetDefectColor_MapsAllNineClassesToExactColors(DefectClass defectClass, double b, double g, double r)
    {
        var color = ImageAnnotationService.GetDefectColor(defectClass);
        color.Val0.Should().Be(b);
        color.Val1.Should().Be(g);
        color.Val2.Should().Be(r);
    }

    [Theory]
    [InlineData(DefectClass.SpuriousCopper, true)]
    [InlineData(DefectClass.Spur, true)]
    [InlineData(DefectClass.ConductorForeignObject, true)]
    [InlineData(DefectClass.Short, false)]
    [InlineData(DefectClass.ConductorScratch, false)]
    public void GetContrastingTextColor_ReturnsExpectedContrast(DefectClass defectClass, bool expectsDarkText)
    {
        var textColor = ImageAnnotationService.GetContrastingTextColor(defectClass);
        if (expectsDarkText)
        {
            textColor.Val0.Should().BeLessThan(50);
        }
        else
        {
            textColor.Val0.Should().Be(255);
        }
    }

    [Fact]
    public void MatToBitmapSource_WhenMatIsEmpty_ReturnsNull()
    {
        using var emptyMat = new OpenCvSharp.Mat();
        var result = _sut.MatToBitmapSource(emptyMat);
        result.Should().BeNull();
    }

    [Fact]
    public void MatToBitmapSource_WhenMatIsValid_ReturnsFrozenBitmap()
    {
        using var validMat = new OpenCvSharp.Mat(100, 100, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(100, 150, 200));
        var result = _sut.MatToBitmapSource(validMat);

        result.Should().NotBeNull();
        result!.IsFrozen.Should().BeTrue();
        result.PixelWidth.Should().Be(100);
        result.PixelHeight.Should().Be(100);
    }

    [Fact]
    public void AnnotateMat_WithDefects_ReturnsFrozenBitmapWithAnnotations()
    {
        using var validMat = new OpenCvSharp.Mat(640, 640, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(50, 50, 50));
        var defects = new[]
        {
            new DetectedDefect(Guid.NewGuid(), Guid.NewGuid(), "short", 0.92f, new BoundingBox(0.2f, 0.2f, 0.3f, 0.3f)),
            new DetectedDefect(Guid.NewGuid(), Guid.NewGuid(), "open", 0.88f, new BoundingBox(0.5f, 0.5f, 0.2f, 0.2f))
        };

        var result = _sut.AnnotateMat(validMat, defects);

        result.Should().NotBeNull();
        result!.IsFrozen.Should().BeTrue();
        result.PixelWidth.Should().Be(640);
        result.PixelHeight.Should().Be(640);
    }

    [Fact]
    public void AnnotateMat_BenchmarkedConversion_CompletesWithinPerformanceThreshold()
    {
        using var mat = new OpenCvSharp.Mat(640, 640, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(100, 100, 100));
        var defects = new[]
        {
            new DetectedDefect(Guid.NewGuid(), Guid.NewGuid(), "short", 0.90f, new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f))
        };

        // Warmup
        _ = _sut.AnnotateMat(mat, defects);

        long minElapsedMs = long.MaxValue;
        System.Windows.Media.Imaging.BitmapSource? result = null;
        for (int i = 0; i < 5; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            result = _sut.AnnotateMat(mat, defects);
            sw.Stop();
            if (sw.ElapsedMilliseconds < minElapsedMs)
            {
                minElapsedMs = sw.ElapsedMilliseconds;
            }
        }

        result.Should().NotBeNull();
        minElapsedMs.Should().BeLessThan(25);
    }
}
