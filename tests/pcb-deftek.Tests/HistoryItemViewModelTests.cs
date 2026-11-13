using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Moq;
using OpenCvSharp;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;
using pcb_deftek.Presentation.ViewModels;
using Xunit;

namespace pcb_deftek.Tests;

public class HistoryItemViewModelTests
{
    private readonly Mock<IInspectionRepository> _repository = new();
    private readonly InspectionSummary _summary;

    public HistoryItemViewModelTests()
    {
        _summary = new InspectionSummary(
            Guid.NewGuid(),
            "Board-A",
            DateTime.UtcNow.AddHours(-2),
            DateTime.UtcNow.AddHours(-1),
            DetectionMode.LiveCamera,
            2,
            "C:/captures/board-a_raw.png");
    }

    private HistoryItemViewModel CreateSut()
    {
        return new HistoryItemViewModel(_summary, _repository.Object);
    }

    private static BitmapSource CreateFrozenBitmap()
    {
        using var mat = new Mat(40, 40, MatType.CV_8UC3, new Scalar(10, 20, 30));
        int stride = (int)mat.Step();
        var bitmap = BitmapSource.Create(
            mat.Width,
            mat.Height,
            96.0,
            96.0,
            PixelFormats.Bgr24,
            null,
            mat.Data,
            stride * mat.Height,
            stride);
        bitmap.Freeze();
        return bitmap;
    }

    [Fact]
    public void InitialState_ExposesSummaryMetadata()
    {
        var sut = CreateSut();

        sut.Id.Should().Be(_summary.Id);
        sut.Name.Should().Be("Board-A");
        sut.DefectCount.Should().Be(2);
        sut.StatusText.Should().Be("FAIL");
        sut.IsPass.Should().BeFalse();
        sut.DefectCountText.Should().Be("2 Defects");
        sut.ModeText.Should().Be("Live Camera");
        sut.IsSelected.Should().BeFalse();
        sut.HasThumbnail.Should().BeFalse();
        sut.IsImageUnavailable.Should().BeFalse();
    }

    [Fact]
    public void BeginRename_SeedsEditedNameAndEntersEditMode()
    {
        var sut = CreateSut();

        sut.BeginRename();

        sut.IsEditingName.Should().BeTrue();
        sut.EditedName.Should().Be("Board-A");
    }

    [Fact]
    public void CancelRename_RestoresPersistedName()
    {
        var sut = CreateSut();
        sut.BeginRename();
        sut.EditedName = "Changed";

        sut.CancelRename();

        sut.IsEditingName.Should().BeFalse();
        sut.EditedName.Should().Be("Board-A");
        sut.Name.Should().Be("Board-A");
    }

    [Fact]
    public async Task CommitRenameAsync_WithValidName_PersistsAndUpdatesName()
    {
        _repository
            .Setup(repo => repo.UpdateNameAsync(_summary.Id, "Board-B", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var sut = CreateSut();
        sut.BeginRename();
        sut.EditedName = "  Board-B  ";

        var committed = await sut.CommitRenameAsync();

        committed.Should().BeTrue();
        sut.Name.Should().Be("Board-B");
        sut.EditedName.Should().Be("Board-B");
        sut.IsEditingName.Should().BeFalse();
        _repository.Verify(
            repo => repo.UpdateNameAsync(_summary.Id, "Board-B", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CommitRenameAsync_WithWhitespace_RevertsWithoutRepositoryCall()
    {
        var sut = CreateSut();
        sut.BeginRename();
        sut.EditedName = "   ";

        var committed = await sut.CommitRenameAsync();

        committed.Should().BeFalse();
        sut.Name.Should().Be("Board-A");
        sut.IsEditingName.Should().BeFalse();
        _repository.Verify(
            repo => repo.UpdateNameAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CommitRenameAsync_WhenRepositoryReturnsFalse_RevertsAndRaisesFailure()
    {
        _repository
            .Setup(repo => repo.UpdateNameAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var sut = CreateSut();
        string? failure = null;
        sut.RenameFailed += (_, message) => failure = message;
        sut.BeginRename();
        sut.EditedName = "Board-B";

        var committed = await sut.CommitRenameAsync();

        committed.Should().BeFalse();
        sut.Name.Should().Be("Board-A");
        sut.IsEditingName.Should().BeFalse();
        failure.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CommitRenameAsync_WhenRepositoryThrows_RevertsAndRaisesFailure()
    {
        _repository
            .Setup(repo => repo.UpdateNameAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage offline"));
        var sut = CreateSut();
        string? failure = null;
        sut.RenameFailed += (_, message) => failure = message;
        sut.BeginRename();
        sut.EditedName = "Board-B";

        var committed = await sut.CommitRenameAsync();

        committed.Should().BeFalse();
        sut.Name.Should().Be("Board-A");
        failure.Should().Contain("storage offline");
    }

    [Fact]
    public async Task LoadThumbnailAsync_WhenServiceReturnsBitmap_SetsThumbnail()
    {
        var bitmap = CreateFrozenBitmap();
        var thumbnailService = new Mock<IHistoryThumbnailService>();
        thumbnailService
            .Setup(service => service.GetThumbnailAsync(_summary.ImagePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bitmap);
        var sut = CreateSut();

        await sut.LoadThumbnailAsync(thumbnailService.Object);

        sut.Thumbnail.Should().BeSameAs(bitmap);
        sut.HasThumbnail.Should().BeTrue();
        sut.IsImageUnavailable.Should().BeFalse();
    }

    [Fact]
    public async Task LoadThumbnailAsync_WhenServiceReturnsNull_MarksImageUnavailable()
    {
        var thumbnailService = new Mock<IHistoryThumbnailService>();
        thumbnailService
            .Setup(service => service.GetThumbnailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BitmapSource?)null);
        var sut = CreateSut();

        await sut.LoadThumbnailAsync(thumbnailService.Object);

        sut.Thumbnail.Should().BeNull();
        sut.IsImageUnavailable.Should().BeTrue();
    }
}
