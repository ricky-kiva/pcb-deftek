using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using pcb_deftek.Infrastructure.Services;
using Xunit;

namespace pcb_deftek.Tests;

public class FilePickerServiceTests
{
    [Fact]
    public async Task PickFilesAsync_WhenFilesSelected_ReturnsChosenPaths()
    {
        // Arrange
        var expectedPaths = new[] { @"C:\pcb\board1.png", @"C:\pcb\board2.jpg" };
        var sut = new WpfFilePickerService(
            fileDialogHandler: (title, filter, multiselect) => expectedPaths);

        // Act
        var result = await sut.PickFilesAsync("Select Images", null, true);

        // Assert
        result.Should().BeEquivalentTo(expectedPaths);
    }

    [Fact]
    public async Task PickFilesAsync_WhenCancelled_ReturnsEmptyList()
    {
        // Arrange
        var sut = new WpfFilePickerService(
            fileDialogHandler: (title, filter, multiselect) => Array.Empty<string>());

        // Act
        var result = await sut.PickFilesAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task PickFolderAsync_WhenFolderSelected_ReturnsFolderPath()
    {
        // Arrange
        var expectedFolder = @"C:\pcb\batch_01";
        var sut = new WpfFilePickerService(
            folderDialogHandler: title => expectedFolder);

        // Act
        var result = await sut.PickFolderAsync("Select Batch Folder");

        // Assert
        result.Should().Be(expectedFolder);
    }

    [Fact]
    public async Task PickFolderAsync_WhenCancelled_ReturnsNull()
    {
        // Arrange
        var sut = new WpfFilePickerService(
            folderDialogHandler: title => null);

        // Act
        var result = await sut.PickFolderAsync();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void DefaultImageFilter_ContainsAllRequiredExtensions()
    {
        // Assert
        IFilePickerService.DefaultImageFilter.Should().Contain("*.jpg");
        IFilePickerService.DefaultImageFilter.Should().Contain("*.jpeg");
        IFilePickerService.DefaultImageFilter.Should().Contain("*.png");
        IFilePickerService.DefaultImageFilter.Should().Contain("*.bmp");
        IFilePickerService.DefaultImageFilter.Should().Contain("*.webp");
    }
}
