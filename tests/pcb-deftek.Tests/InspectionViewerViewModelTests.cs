using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCvSharp;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;
using pcb_deftek.Presentation.Services;
using pcb_deftek.Presentation.ViewModels;
using Xunit;

namespace pcb_deftek.Tests;

public class InspectionViewerViewModelTests : IDisposable
{
    private readonly string _testRoot;
    private readonly Mock<IInspectionRepository> _repository = new();
    private readonly Mock<IViewportCropService> _cropService = new();
    private readonly Mock<IInspectionDeletionService> _deletionService = new();
    private readonly Mock<IConfirmationService> _confirmationService = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly string _imagePath;

    public InspectionViewerViewModelTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"pcb-deftek-viewer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
        _imagePath = Path.Combine(_testRoot, "board.png");

        using var mat = new Mat(100, 200, MatType.CV_8UC3, Scalar.All(180));
        Cv2.ImWrite(_imagePath, mat).Should().BeTrue();

        _cropService
            .Setup(service => service.CropAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<NormalizedRect>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Path.Combine(_testRoot, "crop.png"));

        _notificationService
            .Setup(service => service.ShowToastAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _deletionService
            .Setup(service => service.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InspectionDeletionResult(1, 0, Array.Empty<string>()));

        _confirmationService
            .Setup(service => service.ConfirmAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
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
            // Best effort.
        }
    }

    private InspectionViewerViewModel CreateSut() =>
        new(
            _repository.Object,
            _cropService.Object,
            _deletionService.Object,
            _confirmationService.Object,
            _notificationService.Object,
            NullLogger<InspectionViewerViewModel>.Instance);

    private static DetectedDefect CreateDefect(
        Guid inspectionId,
        DefectClass defectClass,
        float confidence,
        float x,
        float y,
        float width,
        float height) =>
        new(
            Guid.NewGuid(),
            inspectionId,
            DefectKnowledgeBase.GetInfo(defectClass).Code,
            confidence,
            new BoundingBox(x, y, width, height));

    private InspectionRecord CreateRecord(Guid id, string name, string imagePath, params DetectedDefect[] defects) =>
        new(
            id,
            name,
            DateTime.UtcNow.AddMinutes(-5),
            DateTime.UtcNow,
            DetectionMode.SingleFile,
            imagePath,
            defects);

    private void SetupRecord(InspectionRecord? record)
    {
        _repository
            .Setup(repo => repo.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
    }

    [Fact]
    public async Task LoadAsync_BuildsMarkersSummariesAndMetadata()
    {
        var id = Guid.NewGuid();
        var shortOne = CreateDefect(id, DefectClass.Short, 0.8f, 0.1f, 0.2f, 0.3f, 0.4f);
        var shortTwo = CreateDefect(id, DefectClass.Short, 0.9f, 0.5f, 0.5f, 0.2f, 0.2f);
        var open = CreateDefect(id, DefectClass.Open, 0.7f, 0.2f, 0.2f, 0.2f, 0.2f);
        var record = new InspectionRecord(
            id,
            "Board A",
            DateTime.UtcNow.AddMinutes(-10),
            DateTime.UtcNow,
            DetectionMode.SingleFile,
            _imagePath,
            new[] { shortOne, shortTwo, open });
        SetupRecord(record);
        var sut = CreateSut();

        await sut.LoadAsync(id);

        sut.CurrentName.Should().Be("Board A");
        sut.ImageSource.Should().NotBeNull();
        sut.IsImageUnavailable.Should().BeFalse();
        sut.IsLoading.Should().BeFalse();
        sut.FormattedTimestamp.Should().Be(
            record.CreatedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        sut.DefectMarkers.Should().HaveCount(3);
        sut.DefectMarkers[0].NormalizedBounds.Should().Be(shortOne.BoundingBox);
        sut.DefectMarkers[0].PixelBounds.X.Should().BeApproximately(20, 0.001);
        sut.DefectMarkers[0].PixelBounds.Y.Should().BeApproximately(20, 0.001);
        sut.DefectMarkers[0].PixelBounds.Width.Should().BeApproximately(60, 0.001);
        sut.DefectMarkers[0].PixelBounds.Height.Should().BeApproximately(40, 0.001);
        sut.ClassSummaries.Should().HaveCount(2);
        sut.ClassSummaries[0].Abbreviation.Should().Be("SH");
        sut.ClassSummaries[0].Count.Should().Be(2);
        sut.ClassSummaries[0].HighestConfidence.Should().BeApproximately(0.9f, 0.0001f);
        sut.ClassSummaries[1].Abbreviation.Should().Be("OP");
        sut.ClassSummaries[1].Count.Should().Be(1);
    }

    [Fact]
    public async Task LoadAsync_WithMissingImage_MarksImageUnavailableButKeepsRecord()
    {
        string missing = Path.Combine(_testRoot, "missing.png");
        var id = Guid.NewGuid();
        var record = CreateRecord(
            id,
            "Missing image",
            missing,
            CreateDefect(id, DefectClass.Open, 0.7f, 0.2f, 0.2f, 0.2f, 0.2f));
        SetupRecord(record);
        var sut = CreateSut();

        await sut.LoadAsync(id);

        sut.ImageSource.Should().BeNull();
        sut.IsImageUnavailable.Should().BeTrue();
        sut.CurrentName.Should().Be("Missing image");
        sut.DefectMarkers.Should().ContainSingle();
        sut.CropViewportCommand.CanExecute(NormalizedRect.Full).Should().BeFalse();
        sut.DeleteImageCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_WhenRecordMissing_SetsUnavailableState()
    {
        SetupRecord(null);
        var sut = CreateSut();

        await sut.LoadAsync(Guid.NewGuid());

        sut.ImageSource.Should().BeNull();
        sut.IsImageUnavailable.Should().BeTrue();
        sut.CurrentName.Should().BeEmpty();
        sut.DefectMarkers.Should().BeEmpty();
        sut.ClassSummaries.Should().BeEmpty();
        sut.DeleteImageCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task SelectMarker_ExposesFocusedDiagnostics()
    {
        var id = Guid.NewGuid();
        var record = CreateRecord(
            id,
            "Focused board",
            _imagePath,
            CreateDefect(id, DefectClass.Short, 0.8f, 0.1f, 0.2f, 0.3f, 0.4f));
        SetupRecord(record);
        var sut = CreateSut();
        await sut.LoadAsync(id);
        var info = DefectKnowledgeBase.GetInfo(DefectClass.Short);

        sut.SelectMarker(sut.DefectMarkers[0]);

        sut.IsDefectFocused.Should().BeTrue();
        sut.SelectedMarker.Should().Be(sut.DefectMarkers[0]);
        sut.DefectMarkers[0].IsSelected.Should().BeTrue();
        sut.FocusedClassName.Should().Be("Short");
        sut.FocusedConfidenceText.Should().Be("80%");
        sut.FocusedSeverityText.Should().Be("Critical");
        sut.FocusedRootCause.Should().Be(info.RootCause);
        sut.FocusedImpact.Should().Be(info.Impact);
        sut.FocusedAction.Should().Be(info.Action);

        sut.SelectMarker(null);

        sut.IsDefectFocused.Should().BeFalse();
        sut.FocusedClassName.Should().BeEmpty();
        sut.FocusedRootCause.Should().BeEmpty();
    }

    [Fact]
    public async Task CropViewportCommand_WhenImageLoaded_CropsAndShowsToast()
    {
        var id = Guid.NewGuid();
        var record = CreateRecord(id, "Crop board", _imagePath);
        SetupRecord(record);
        var sut = CreateSut();
        await sut.LoadAsync(id);
        var region = new NormalizedRect(0.1, 0.2, 0.3, 0.4);

        await sut.CropViewportCommand.ExecuteAsync(region);

        _cropService.Verify(service => service.CropAsync(
            _imagePath,
            "Crop board",
            region,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
        _notificationService.Verify(service => service.ShowToastAsync(
            "Crop saved",
            It.Is<string>(message => message.Contains("crop.png", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CropViewportCommand_WhenCropFails_ReportsWithoutThrowing()
    {
        var id = Guid.NewGuid();
        var record = CreateRecord(id, "Crop board", _imagePath);
        SetupRecord(record);
        _cropService
            .Setup(service => service.CropAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<NormalizedRect>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ImageProcessingException("Could not write crop.", "WritePng"));
        var sut = CreateSut();
        await sut.LoadAsync(id);

        Func<Task> act = () => sut.CropViewportCommand.ExecuteAsync(NormalizedRect.Full);

        await act.Should().NotThrowAsync();
        _notificationService.Verify(service => service.ShowToastAsync(
            "Crop failed",
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CropViewportCommand_WhenViewerClosesBeforeFailure_DoesNotThrow()
    {
        var id = Guid.NewGuid();
        var record = CreateRecord(id, "Crop board", _imagePath);
        SetupRecord(record);
        var cropCompletion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _cropService
            .Setup(service => service.CropAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<NormalizedRect>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(cropCompletion.Task);
        var sut = CreateSut();
        await sut.LoadAsync(id);

        Task crop = sut.CropViewportCommand.ExecuteAsync(NormalizedRect.Full);
        sut.Close();
        cropCompletion.SetException(new ImageProcessingException("Crop failed after close.", "WritePng"));

        Func<Task> act = () => crop;
        await act.Should().NotThrowAsync();
        _notificationService.Verify(service => service.ShowToastAsync(
            "Crop failed",
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteImageCommand_WhenConfirmed_DeletesClosesAndRaisesEvent()
    {
        var id = Guid.NewGuid();
        var record = CreateRecord(id, "Delete board", _imagePath);
        SetupRecord(record);
        var sut = CreateSut();
        await sut.LoadAsync(id);
        Guid? deletedId = null;
        sut.Deleted += (_, recordId) => deletedId = recordId;

        await sut.DeleteImageCommand.ExecuteAsync(null);

        _confirmationService.Verify(service => service.ConfirmAsync(
            It.IsAny<string>(),
            It.Is<string>(message => message.Contains("original source images are kept", StringComparison.OrdinalIgnoreCase)),
            "Delete",
            It.IsAny<CancellationToken>()), Times.Once);
        _deletionService.Verify(service => service.DeleteAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        deletedId.Should().Be(id);
        sut.CurrentName.Should().BeEmpty();
        sut.ImageSource.Should().BeNull();
        sut.DefectMarkers.Should().BeEmpty();
        sut.ClassSummaries.Should().BeEmpty();
        _notificationService.Verify(service => service.ShowToastAsync(
            "Deleted Inspection",
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteImageCommand_WhenCancelled_ChangesNothing()
    {
        var id = Guid.NewGuid();
        var record = CreateRecord(id, "Cancel board", _imagePath);
        SetupRecord(record);
        _confirmationService
            .Setup(service => service.ConfirmAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var sut = CreateSut();
        await sut.LoadAsync(id);

        await sut.DeleteImageCommand.ExecuteAsync(null);

        _deletionService.Verify(service => service.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        sut.CurrentName.Should().Be("Cancel board");
        sut.ImageSource.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteImageCommand_WhenDeletionFails_KeepsViewerState()
    {
        var id = Guid.NewGuid();
        var record = CreateRecord(id, "Failure board", _imagePath);
        SetupRecord(record);
        _deletionService
            .Setup(service => service.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database unavailable."));
        var sut = CreateSut();
        await sut.LoadAsync(id);

        await sut.DeleteImageCommand.ExecuteAsync(null);

        sut.CurrentName.Should().Be("Failure board");
        sut.ImageSource.Should().NotBeNull();
        sut.IsDeleting.Should().BeFalse();
        sut.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        _notificationService.Verify(service => service.ShowToastAsync(
            "Deletion failed",
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Close_ClearsViewerState()
    {
        var id = Guid.NewGuid();
        var record = CreateRecord(id, "Close board", _imagePath);
        SetupRecord(record);
        var sut = CreateSut();
        await sut.LoadAsync(id);
        sut.SelectMarker(sut.DefectMarkers.FirstOrDefault());

        sut.Close();

        sut.CurrentName.Should().BeEmpty();
        sut.ImageSource.Should().BeNull();
        sut.DefectMarkers.Should().BeEmpty();
        sut.ClassSummaries.Should().BeEmpty();
        sut.SelectedMarker.Should().BeNull();
        sut.IsLoading.Should().BeFalse();
        sut.IsImageUnavailable.Should().BeFalse();
    }

    [Fact]
    public async Task Rename_UsesNameEditorAndUpdatesCurrentName()
    {
        var id = Guid.NewGuid();
        var record = CreateRecord(id, "Old name", _imagePath);
        SetupRecord(record);
        _repository
            .Setup(repo => repo.UpdateNameAsync(id, "New name", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = CreateSut();
        await sut.LoadAsync(id);

        sut.BeginRenameCommand.Execute(null);
        sut.EditedName = "New name";
        await sut.CommitRenameCommand.ExecuteAsync(null);

        sut.CurrentName.Should().Be("New name");
        _repository.Verify(repo => repo.UpdateNameAsync(id, "New name", It.IsAny<CancellationToken>()), Times.Once);
    }
}
