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
}
