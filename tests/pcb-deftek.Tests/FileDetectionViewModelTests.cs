using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Inference;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;
using pcb_deftek.Infrastructure.Vision;
using pcb_deftek.Presentation.ViewModels;
using Xunit;

namespace pcb_deftek.Tests;

public class FileDetectionViewModelTests
{
    private readonly Mock<IFileDiscoveryService> _mockDiscovery;
    private readonly Mock<IFilePickerService> _mockPicker;
    private readonly Mock<IBatchInspectionEngine> _mockEngine;
    private readonly Mock<INotificationService> _mockNotification;
    private readonly Mock<IUserSettingsService> _mockSettings;
    private readonly Mock<IImageAnnotationService> _mockAnnotation;

    public FileDetectionViewModelTests()
    {
        _mockDiscovery = new Mock<IFileDiscoveryService>();
        _mockPicker = new Mock<IFilePickerService>();
        _mockEngine = new Mock<IBatchInspectionEngine>();
        _mockNotification = new Mock<INotificationService>();
        _mockSettings = new Mock<IUserSettingsService>();
        _mockAnnotation = new Mock<IImageAnnotationService>();

        _mockSettings
            .Setup(s => s.LoadSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { ConfidenceThreshold = 0.35 });
    }

    private FileDetectionViewModel CreateSut()
    {
        return new FileDetectionViewModel(
            _mockDiscovery.Object,
            _mockPicker.Object,
            _mockEngine.Object,
            _mockNotification.Object,
            _mockSettings.Object,
            _mockAnnotation.Object,
            NullLogger<FileDetectionViewModel>.Instance);
    }

    [Fact]
    public void InitialState_PropertiesHaveExpectedDefaults()
    {
        // Act
        var sut = CreateSut();

        // Assert
        sut.Title.Should().Be("File-Based Detection");
        sut.QueueItems.Should().BeEmpty();
        sut.TotalCount.Should().Be(0);
        sut.ProcessedCount.Should().Be(0);
        sut.PassCount.Should().Be(0);
        sut.FailCount.Should().Be(0);
        sut.ErrorCount.Should().Be(0);
        sut.PercentComplete.Should().Be(0);
        sut.EtaText.Should().BeNull();
        sut.IsProcessing.Should().BeFalse();
        sut.HasItems.Should().BeFalse();
        sut.CanStart.Should().BeFalse();
        sut.CanCancel.Should().BeFalse();
        sut.StatusText.Should().Be("Ready");
    }

    [Fact]
    public async Task OnNavigatedToAsync_LoadsUserConfidenceThreshold()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.OnNavigatedToAsync();

        // Assert
        sut.ConfidenceThreshold.Should().BeApproximately(0.35, 0.001);
    }

    [Fact]
    public async Task BrowseFilesCommand_WhenFilesSelected_AddsDiscoveredItemsToQueue()
    {
        // Arrange
        var rawPaths = new[] { @"C:\pcb\b1.jpg", @"C:\pcb\b2.png" };
        _mockPicker
            .Setup(p => p.PickFilesAsync(It.IsAny<string?>(), It.IsAny<string?>(), true))
            .ReturnsAsync(rawPaths);

        _mockDiscovery
            .Setup(d => d.DiscoverImageFilesAsync(rawPaths, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawPaths);

        var sut = CreateSut();

        // Act
        await sut.BrowseFilesCommand.ExecuteAsync(null);

        // Assert
        sut.QueueItems.Should().HaveCount(2);
        sut.TotalCount.Should().Be(2);
        sut.HasItems.Should().BeTrue();
        sut.CanStart.Should().BeTrue();
        sut.QueueItems[0].FileName.Should().Be("b1.jpg");
        sut.QueueItems[0].Status.Should().Be(BatchItemStatus.Pending);
    }

    [Fact]
    public async Task BrowseFolderCommand_WhenFolderSelected_DiscoversAndPopulatesQueue()
    {
        // Arrange
        var folder = @"C:\pcb\folder";
        var discovered = new[] { @"C:\pcb\folder\1.jpg", @"C:\pcb\folder\2.jpg" };

        _mockPicker
            .Setup(p => p.PickFolderAsync(It.IsAny<string?>()))
            .ReturnsAsync(folder);

        _mockDiscovery
            .Setup(d => d.DiscoverImageFilesAsync(folder, It.IsAny<CancellationToken>()))
            .ReturnsAsync(discovered);

        var sut = CreateSut();

        // Act
        await sut.BrowseFolderCommand.ExecuteAsync(null);

        // Assert
        sut.QueueItems.Should().HaveCount(2);
        sut.TotalCount.Should().Be(2);
        sut.HasItems.Should().BeTrue();
        sut.CanStart.Should().BeTrue();
    }

    [Fact]
    public async Task DropFilesCommand_WhenPathsDropped_PopulatesQueueAndDeduplicates()
    {
        // Arrange
        var dropped = new[] { @"C:\pcb\b1.jpg", @"C:\pcb\b1.jpg" };
        var discovered = new[] { @"C:\pcb\b1.jpg" };

        _mockDiscovery
            .Setup(d => d.DiscoverImageFilesAsync(dropped, It.IsAny<CancellationToken>()))
            .ReturnsAsync(discovered);

        var sut = CreateSut();

        // Act
        await sut.DropFilesCommand.ExecuteAsync(dropped);

        // Assert
        sut.QueueItems.Should().HaveCount(1);
        sut.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task ClearQueueCommand_WhenNotProcessing_ClearsItemsAndResetsState()
    {
        // Arrange
        var files = new[] { @"C:\pcb\b1.jpg" };
        _mockDiscovery
            .Setup(d => d.DiscoverImageFilesAsync(files, It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        var sut = CreateSut();
        await sut.DropFilesCommand.ExecuteAsync(files);
        sut.QueueItems.Should().HaveCount(1);

        // Act
        sut.ClearQueueCommand.Execute(null);

        // Assert
        sut.QueueItems.Should().BeEmpty();
        sut.TotalCount.Should().Be(0);
        sut.HasItems.Should().BeFalse();
        sut.CanStart.Should().BeFalse();
    }

    [Fact]
    public async Task StartBatchCommand_ExecutesEngineAndDispatchesToastNotification()
    {
        // Arrange
        var files = new[] { @"C:\pcb\b1.jpg", @"C:\pcb\b2.jpg" };
        _mockDiscovery
            .Setup(d => d.DiscoverImageFilesAsync(files, It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        var completedItems = new[]
        {
            new BatchInspectionItem(files[0], "b1.jpg", BatchItemStatus.Pass, 0, TimeSpan.FromMilliseconds(50), null, Guid.NewGuid()),
            new BatchInspectionItem(files[1], "b2.jpg", BatchItemStatus.Fail, 2, TimeSpan.FromMilliseconds(60), null, Guid.NewGuid())
        };

        var summary = new BatchInspectionSummary(
            Status: BatchStatus.Completed,
            TotalCount: 2,
            ProcessedCount: 2,
            PassCount: 1,
            FailCount: 1,
            ErrorCount: 0,
            TotalDuration: TimeSpan.FromSeconds(1.5),
            Items: completedItems);

        _mockEngine
            .Setup(e => e.ExecuteBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IProgress<BatchProgressReport>?>(),
                It.IsAny<float?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<string>, IProgress<BatchProgressReport>?, float?, CancellationToken>((paths, progress, thresh, token) =>
            {
                progress?.Report(new BatchProgressReport(
                    TotalCount: 2,
                    ProcessedCount: 1,
                    PassCount: 1,
                    FailCount: 0,
                    ErrorCount: 0,
                    ElapsedTime: TimeSpan.FromSeconds(0.5),
                    EstimatedTimeRemaining: TimeSpan.FromSeconds(0.5),
                    CurrentFileName: "b1.jpg",
                    LatestCompletedItem: completedItems[0]));

                progress?.Report(new BatchProgressReport(
                    TotalCount: 2,
                    ProcessedCount: 2,
                    PassCount: 1,
                    FailCount: 1,
                    ErrorCount: 0,
                    ElapsedTime: TimeSpan.FromSeconds(1.5),
                    EstimatedTimeRemaining: TimeSpan.Zero,
                    CurrentFileName: "b2.jpg",
                    LatestCompletedItem: completedItems[1]));
            })
            .ReturnsAsync(summary);

        var sut = CreateSut();
        await sut.DropFilesCommand.ExecuteAsync(files);

        // Act
        await sut.StartBatchCommand.ExecuteAsync(null);

        // Assert
        sut.IsProcessing.Should().BeFalse();
        sut.ProcessedCount.Should().Be(2);
        sut.PassCount.Should().Be(1);
        sut.FailCount.Should().Be(1);
        sut.PercentComplete.Should().Be(100);
        sut.StatusText.Should().Contain("Completed");

        sut.QueueItems[0].Status.Should().Be(BatchItemStatus.Pass);
        sut.QueueItems[1].Status.Should().Be(BatchItemStatus.Fail);

        _mockNotification.Verify(
            n => n.ShowToastAsync(
                It.Is<string>(t => t.Contains("Batch Inspection")),
                It.Is<string>(m => m.Contains("2") && m.Contains("1 passed")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CancelBatchCommand_WhenActive_RequestsCancellationAndUpdatesStatus()
    {
        // Arrange
        var files = new[] { @"C:\pcb\b1.jpg", @"C:\pcb\b2.jpg" };
        _mockDiscovery
            .Setup(d => d.DiscoverImageFilesAsync(files, It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        var tcs = new TaskCompletionSource<BatchInspectionSummary>();

        _mockEngine
            .Setup(e => e.ExecuteBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IProgress<BatchProgressReport>?>(),
                It.IsAny<float?>(),
                It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var sut = CreateSut();
        await sut.DropFilesCommand.ExecuteAsync(files);

        // Act: Start batch asynchronously (will await engine task)
        var startTask = sut.StartBatchCommand.ExecuteAsync(null);

        sut.IsProcessing.Should().BeTrue();
        sut.CanCancel.Should().BeTrue();

        // Act: Trigger Cancel
        sut.CancelBatchCommand.Execute(null);

        sut.CanCancel.Should().BeFalse(); // Cancel command disables immediately to prevent duplicate clicks

        // Complete engine task with Cancelled summary
        var cancelledSummary = new BatchInspectionSummary(
            Status: BatchStatus.Cancelled,
            TotalCount: 2,
            ProcessedCount: 1,
            PassCount: 1,
            FailCount: 0,
            ErrorCount: 0,
            TotalDuration: TimeSpan.FromSeconds(1.0),
            Items: Array.Empty<BatchInspectionItem>());

        tcs.SetResult(cancelledSummary);
        await startTask;

        // Assert
        sut.IsProcessing.Should().BeFalse();
        sut.StatusText.Should().Contain("Cancelled");
    }

    [Fact]
    public async Task DropFilesCommand_WhenCalledFromBackgroundThread_DoesNotThrowAndPopulatesQueue()
    {
        // Arrange
        var files = new[] { @"C:\pcb\bg1.jpg", @"C:\pcb\bg2.jpg" };
        _mockDiscovery
            .Setup(d => d.DiscoverImageFilesAsync(files, It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        var sut = CreateSut();

        // Act: Invoke DropFilesCommand from a background threadpool thread
        await Task.Run(async () =>
        {
            await sut.DropFilesCommand.ExecuteAsync(files);
        });

        // Assert
        sut.QueueItems.Should().HaveCount(2);
        sut.TotalCount.Should().Be(2);
    }

    [Fact]
    public void FileDetectionQueueItemViewModel_DefectSummary_DisplaysFormattedDefectsOrClean()
    {
        // Arrange
        var item = new FileDetectionQueueItemViewModel(@"C:\pcb\board.jpg");

        item.Status.Should().Be(BatchItemStatus.Pending);
        item.DefectSummary.Should().Be("Queued");

        // Pass state
        item.Status = BatchItemStatus.Pass;
        item.DefectSummary.Should().Be("Clean (No defects)");
        item.StatusBadgeTooltip.Should().Contain("Clean");

        // Fail state with defects
        var detected = new[]
        {
            new DetectedDefect(Guid.NewGuid(), Guid.NewGuid(), "hole_breakout", 0.92f, new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f))
        };
        var result = new InferenceResult(
            Guid.NewGuid(),
            detected,
            new LetterboxTransform(640, 640, 640, 640, 1.0f, 0, 0),
            ExecutionDevice.Cpu,
            new InferenceTiming(5, 20, 2, 27));

        item.UpdateFrom(new BatchInspectionItem(@"C:\pcb\board.jpg", "board.jpg", BatchItemStatus.Fail, 1, TimeSpan.FromMilliseconds(169), null, Guid.NewGuid(), result));

        item.Status.Should().Be(BatchItemStatus.Fail);
        item.DefectCount.Should().Be(1);
        item.DefectSummary.Should().Contain("Hole breakout");
        item.DefectSummary.Should().Contain("92%");
        item.StatusBadgeTooltip.Should().Contain("Inspection Failed");
    }

    [Fact]
    public void OpenPreviewCommand_And_ClosePreviewCommand_ManageModalState()
    {
        // Arrange
        var sut = CreateSut();
        var item = new FileDetectionQueueItemViewModel(@"C:\pcb\board1.jpg");

        sut.IsPreviewModalOpen.Should().BeFalse();
        sut.PreviewItem.Should().BeNull();

        // Act: Open preview
        sut.OpenPreviewCommand.Execute(item);

        // Assert
        sut.IsPreviewModalOpen.Should().BeTrue();
        sut.PreviewItem.Should().BeSameAs(item);

        // Act: Close preview
        sut.ClosePreviewCommand.Execute(null);

        // Assert
        sut.IsPreviewModalOpen.Should().BeFalse();
        sut.PreviewItem.Should().BeNull();
        sut.LargePreviewImage.Should().BeNull();
    }

    [Fact]
    public void FileDetectionQueueItemViewModel_MultipleDefects_TogglesExpansion()
    {
        // Arrange
        var item = new FileDetectionQueueItemViewModel(@"C:\pcb\board1.jpg");
        var defects = new[]
        {
            new DetectedDefect(Guid.NewGuid(), Guid.NewGuid(), "hole_breakout", 0.95f, new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f)),
            new DetectedDefect(Guid.NewGuid(), Guid.NewGuid(), "spur", 0.85f, new BoundingBox(0.3f, 0.3f, 0.2f, 0.2f))
        };
        var result = new InferenceResult(
            Guid.NewGuid(),
            defects,
            new LetterboxTransform(640, 640, 640, 640, 1.0f, 0, 0),
            ExecutionDevice.Cpu,
            new InferenceTiming(5, 20, 2, 27));

        item.UpdateFrom(new BatchInspectionItem(@"C:\pcb\board1.jpg", "board1.jpg", BatchItemStatus.Fail, 2, TimeSpan.FromMilliseconds(200), null, Guid.NewGuid(), result));

        // Assert initial collapsed state
        item.HasMultipleDefects.Should().BeTrue();
        item.IsDefectsExpanded.Should().BeFalse();
        item.CollapsedSummaryText.Should().Be("2 defects");
        item.DefectList.Should().HaveCount(2);

        // Act: Expand
        item.ToggleDefectsExpandedCommand.Execute(null);
        item.IsDefectsExpanded.Should().BeTrue();

        // Act: Collapse
        item.ToggleDefectsExpandedCommand.Execute(null);
        item.IsDefectsExpanded.Should().BeFalse();
    }
}
