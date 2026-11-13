using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using pcb_deftek.Infrastructure.Services;
using Xunit;

namespace pcb_deftek.Tests;

public class HistoryThumbnailServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly HistoryThumbnailService _service;

    public HistoryThumbnailServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"pcb-deftek-thumb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
        _service = new HistoryThumbnailService(NullLogger<HistoryThumbnailService>.Instance);
    }

    private string CreateImage(string name, int width, int height)
    {
        string path = Path.Combine(_testRoot, name);
        using var mat = new Mat(height, width, MatType.CV_8UC3, new Scalar(30, 80, 140));
        Cv2.ImWrite(path, mat);
        return path;
    }

    [Fact]
    public async Task GetThumbnailAsync_WithValidImage_ReturnsFrozenBitmapWithinDecodeWidth()
    {
        string path = CreateImage("board.png", 800, 400);

        var thumbnail = await _service.GetThumbnailAsync(path);

        thumbnail.Should().NotBeNull();
        thumbnail!.IsFrozen.Should().BeTrue();
        thumbnail.PixelWidth.Should().BeLessThanOrEqualTo(240);
        thumbnail.PixelWidth.Should().BeGreaterThan(0);
        thumbnail.PixelHeight.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetThumbnailAsync_WithMissingFile_ReturnsNull()
    {
        var thumbnail = await _service.GetThumbnailAsync(Path.Combine(_testRoot, "missing.png"));

        thumbnail.Should().BeNull();
    }

    [Fact]
    public async Task GetThumbnailAsync_WithCorruptFile_ReturnsNull()
    {
        string path = Path.Combine(_testRoot, "corrupt.png");
        await File.WriteAllTextAsync(path, "not an image");

        var thumbnail = await _service.GetThumbnailAsync(path);

        thumbnail.Should().BeNull();
    }

    [Fact]
    public async Task GetThumbnailAsync_WithEmptyPath_ThrowsArgumentException()
    {
        Func<Task> act = async () => await _service.GetThumbnailAsync("   ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetThumbnailAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        string path = CreateImage("cancelled.png", 100, 100);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await _service.GetThumbnailAsync(path, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
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
        catch
        {
            // Best effort cleanup
        }
    }
}
