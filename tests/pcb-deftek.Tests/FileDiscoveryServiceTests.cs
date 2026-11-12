using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using pcb_deftek.Infrastructure.Services;
using Xunit;

namespace pcb_deftek.Tests;

public class FileDiscoveryServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly IFileDiscoveryService _sut;

    public FileDiscoveryServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "pcb-deftek-file-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _sut = new FileDiscoveryService();
    }

    [Theory]
    [InlineData("board.jpg", true)]
    [InlineData("board.JPEG", true)]
    [InlineData("board.png", true)]
    [InlineData("board.PNG", true)]
    [InlineData("board.bmp", true)]
    [InlineData("board.webp", true)]
    [InlineData("board.txt", false)]
    [InlineData("board.pdf", false)]
    [InlineData("board.exe", false)]
    [InlineData("board", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupportedImageFile_ValidatesFileExtensionsCorrectly(string? filename, bool expected)
    {
        // Act
        var result = _sut.IsSupportedImageFile(filename!);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task DiscoverImageFilesAsync_SingleValidFile_ReturnsAbsolutePath()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "board1.png");
        await File.WriteAllBytesAsync(filePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // minimal header

        // Act
        var result = await _sut.DiscoverImageFilesAsync(filePath);

        // Assert
        result.Should().HaveCount(1);
        result[0].Should().Be(Path.GetFullPath(filePath));
    }

    [Fact]
    public async Task DiscoverImageFilesAsync_NonExistentFile_ReturnsEmpty()
    {
        // Arrange
        var nonExistent = Path.Combine(_testDir, "does-not-exist.jpg");

        // Act
        var result = await _sut.DiscoverImageFilesAsync(nonExistent);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverImageFilesAsync_ZeroByteFile_IsIgnored()
    {
        // Arrange
        var zeroByteFile = Path.Combine(_testDir, "empty.jpg");
        await File.WriteAllBytesAsync(zeroByteFile, Array.Empty<byte>());

        // Act
        var result = await _sut.DiscoverImageFilesAsync(zeroByteFile);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverImageFilesAsync_HiddenFile_IsIgnored()
    {
        // Arrange
        var hiddenFile = Path.Combine(_testDir, "hidden.png");
        await File.WriteAllBytesAsync(hiddenFile, new byte[] { 1, 2, 3 });
        File.SetAttributes(hiddenFile, File.GetAttributes(hiddenFile) | FileAttributes.Hidden);

        // Act
        var result = await _sut.DiscoverImageFilesAsync(hiddenFile);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverImageFilesAsync_DirectoryPath_RecursivelyFindsAllSupportedImages()
    {
        // Arrange
        var subDir = Path.Combine(_testDir, "subfolder");
        Directory.CreateDirectory(subDir);

        var file1 = Path.Combine(_testDir, "board1.jpg");
        var file2 = Path.Combine(_testDir, "readme.txt");
        var file3 = Path.Combine(subDir, "board2.webp");
        var file4 = Path.Combine(subDir, "board3.bmp");

        await File.WriteAllBytesAsync(file1, new byte[] { 1 });
        await File.WriteAllBytesAsync(file2, new byte[] { 1 });
        await File.WriteAllBytesAsync(file3, new byte[] { 1 });
        await File.WriteAllBytesAsync(file4, new byte[] { 1 });

        // Act
        var result = await _sut.DiscoverImageFilesAsync(_testDir);

        // Assert
        result.Should().HaveCount(3);
        result.Should().Contain(Path.GetFullPath(file1));
        result.Should().Contain(Path.GetFullPath(file3));
        result.Should().Contain(Path.GetFullPath(file4));
        result.Should().NotContain(Path.GetFullPath(file2));
    }

    [Fact]
    public async Task DiscoverImageFilesAsync_MultipleMixedPaths_DeduplicatesEntries()
    {
        // Arrange
        var file1 = Path.Combine(_testDir, "board.png");
        await File.WriteAllBytesAsync(file1, new byte[] { 1, 2 });

        var inputPaths = new[] { file1, file1, _testDir };

        // Act
        var result = await _sut.DiscoverImageFilesAsync(inputPaths);

        // Assert
        result.Should().HaveCount(1);
        result[0].Should().Be(Path.GetFullPath(file1));
    }

    [Fact]
    public async Task DiscoverImageFilesAsync_WhenCancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = async () => await _sut.DiscoverImageFilesAsync(_testDir, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
