using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using OpenCvSharp;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Common.Storage;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;
using Xunit;

namespace pcb_deftek.Tests;

public class ViewportCropServiceTests : IDisposable
{
    private readonly string _testRoot;

    public ViewportCropServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"pcb-deftek-crop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    private string CreateSourceImage(int width, int height)
    {
        string path = Path.Combine(_testRoot, "source.png");
        using var mat = new Mat(height, width, MatType.CV_8UC3, Scalar.All(200));
        Cv2.ImWrite(path, mat).Should().BeTrue();
        return path;
    }

    [Fact]
    public async Task CropAsync_CropsAtNativeResolution()
    {
        string source = CreateSourceImage(400, 200);
        string outputDir = Path.Combine(_testRoot, "out");
        var sut = new ViewportCropService();

        ViewportCropResult result = await sut.CropAsync(
            source,
            "Board 1",
            new NormalizedRect(0.25, 0.25, 0.5, 0.5),
            outputDir);

        File.Exists(result.FilePath).Should().BeTrue();
        result.PixelWidth.Should().Be(200);
        result.PixelHeight.Should().Be(100);
        result.SourceRegion.Should().Be(new NormalizedRect(0.25, 0.25, 0.5, 0.5));
        using var cropped = Cv2.ImRead(result.FilePath, ImreadModes.Unchanged);
        cropped.Width.Should().Be(200);
        cropped.Height.Should().Be(100);
    }

    [Fact]
    public async Task CropAsync_ClampsRegionToImageBounds()
    {
        string source = CreateSourceImage(200, 100);
        string outputDir = Path.Combine(_testRoot, "out");
        var sut = new ViewportCropService();
        var clamped = NormalizedRect.Clamp(0.9, 0.9, 0.5, 0.5);

        ViewportCropResult result = await sut.CropAsync(source, "edge", clamped, outputDir);

        using var cropped = Cv2.ImRead(result.FilePath, ImreadModes.Unchanged);
        cropped.Width.Should().Be(20);
        cropped.Height.Should().Be(10);
    }

    [Fact]
    public async Task CropAsync_WithEmptyRegion_ThrowsImageProcessingException()
    {
        string source = CreateSourceImage(200, 100);
        var sut = new ViewportCropService();

        Func<Task> act = () => sut.CropAsync(
            source,
            "empty",
            NormalizedRect.Empty,
            Path.Combine(_testRoot, "out"));

        await act.Should().ThrowAsync<ImageProcessingException>();
    }

    [Fact]
    public async Task CropAsync_WithMissingSource_ThrowsImageProcessingException()
    {
        var sut = new ViewportCropService();

        Func<Task> act = () => sut.CropAsync(
            Path.Combine(_testRoot, "missing.png"),
            "missing",
            NormalizedRect.Full,
            Path.Combine(_testRoot, "out"));

        await act.Should().ThrowAsync<ImageProcessingException>();
    }

    [Fact]
    public async Task CropAsync_SanitizesNameAndAvoidsOverwrites()
    {
        string source = CreateSourceImage(100, 100);
        string outputDir = Path.Combine(_testRoot, "out");
        var sut = new ViewportCropService();

        ViewportCropResult first = await sut.CropAsync(
            source,
            "Board: 1/2?",
            NormalizedRect.Full,
            outputDir);
        ViewportCropResult second = await sut.CropAsync(
            source,
            "Board: 1/2?",
            NormalizedRect.Full,
            outputDir);

        string firstName = Path.GetFileName(first.FilePath);
        firstName.Should().NotContainAny(":", "/", "?", "\\");
        firstName.Should().StartWith("Board");
        Path.GetFileNameWithoutExtension(second.FilePath).Should().NotBe(Path.GetFileNameWithoutExtension(first.FilePath));
        File.Exists(first.FilePath).Should().BeTrue();
        File.Exists(second.FilePath).Should().BeTrue();
    }

    [Fact]
    public async Task CropAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        string source = CreateSourceImage(100, 100);
        var sut = new ViewportCropService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => sut.CropAsync(
            source,
            "cancelled",
            NormalizedRect.Full,
            Path.Combine(_testRoot, "out"),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CropAsync_WithoutOverride_UsesConfiguredCropDirectory()
    {
        string source = CreateSourceImage(200, 100);
        string configured = Path.Combine(_testRoot, "configured");
        var settings = new Mock<IUserSettingsService>();
        settings
            .Setup(service => service.LoadSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { ImageOutputDirectory = configured });
        var sut = new ViewportCropService(settingsService: settings.Object);

        ViewportCropResult result = await sut.CropAsync(source, "configured", NormalizedRect.Full);

        Path.GetDirectoryName(result.FilePath).Should().Be(configured);
        File.Exists(result.FilePath).Should().BeTrue();
    }

    [Fact]
    public void ApplicationDirectories_UseTheExpectedDownloadsSubtrees()
    {
        string downloads = KnownFolderPaths.GetDownloadsDirectory();

        KnownFolderPaths.GetCropImagesDirectory().Should()
            .Be(Path.Combine(downloads, "PCB Deftek", "Images"));
        KnownFolderPaths.GetReportExportDirectory().Should()
            .Be(Path.Combine(downloads, "PCB Deftek", "Report"));
        KnownFolderPaths.GetDatabaseBackupDirectory().Should()
            .Be(Path.Combine(downloads, "PCB Deftek", "Backup"));
    }

    [Fact]
    public void GetDownloadsDirectory_ReturnsUsableRootedPath()
    {
        string downloads = KnownFolderPaths.GetDownloadsDirectory();

        downloads.Should().NotBeNullOrWhiteSpace();
        Path.IsPathRooted(downloads).Should().BeTrue();
        Path.GetFileName(downloads.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Should().Be("Downloads");
    }
}
