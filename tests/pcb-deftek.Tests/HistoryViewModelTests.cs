using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCvSharp;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;
using pcb_deftek.Presentation.Services;
using pcb_deftek.Presentation.ViewModels;
using Xunit;

namespace pcb_deftek.Tests;

public class HistoryViewModelTests
{
    private readonly Mock<IInspectionRepository> _repository = new();
    private readonly Mock<IHistoryThumbnailService> _thumbnailService = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<IInspectionDeletionService> _deletionService = new();
    private readonly Mock<IConfirmationService> _confirmationService = new();
    private readonly InspectionViewerViewModel _viewer;

    public HistoryViewModelTests()
    {
        _thumbnailService
            .Setup(service => service.GetThumbnailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BitmapSource?)null);

        _notificationService
            .Setup(service => service.ShowToastAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _deletionService
            .Setup(service => service.DeleteManyAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InspectionDeletionResult(0, 0, Array.Empty<string>()));

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

        _viewer = new InspectionViewerViewModel(
            _repository.Object,
            new Mock<IViewportCropService>().Object,
            _deletionService.Object,
            _confirmationService.Object,
            _notificationService.Object,
            NullLogger<InspectionViewerViewModel>.Instance);
    }

    private HistoryViewModel CreateSut()
    {
        return new HistoryViewModel(
            _repository.Object,
            _thumbnailService.Object,
            _notificationService.Object,
            _deletionService.Object,
            _confirmationService.Object,
            _viewer,
            NullLogger<HistoryViewModel>.Instance);
    }

    private static InspectionSummary CreateSummary(
        string name,
        DateTime createdAtUtc,
        int defectCount = 0,
        DetectionMode mode = DetectionMode.SingleFile)
    {
        return new InspectionSummary(
            Guid.NewGuid(),
            name,
            createdAtUtc,
            createdAtUtc,
            mode,
            defectCount,
            $"images/{name}.png");
    }

    private void SetupPages(IReadOnlyList<InspectionSummary> all, Action<InspectionQueryFilter>? onCall = null)
    {
        _repository
            .Setup(repo => repo.GetSummariesPagedAsync(It.IsAny<InspectionQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InspectionQueryFilter filter, CancellationToken _) =>
            {
                onCall?.Invoke(filter);
                var items = all
                    .Skip((filter.PageNumber - 1) * filter.PageSize)
                    .Take(filter.PageSize)
                    .ToList();
                return new PagedResult<InspectionSummary>(items, filter.PageNumber, filter.PageSize, all.Count);
            });
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    private static DateTime LocalNoonUtc(int dayOffset = 0)
    {
        var localNoon = DateTime.SpecifyKind(
            DateTime.Today.AddDays(dayOffset).AddHours(12),
            DateTimeKind.Local);

        return localNoon.ToUniversalTime();
    }

    [Fact]
    public void InitialState_DoesNotReportEmptyBeforeFirstLoad()
    {
        var sut = CreateSut();

        sut.IsEmpty.Should().BeFalse();
        sut.HasNoMatches.Should().BeFalse();
        sut.IsInitialLoading.Should().BeFalse();
    }

    [Fact]
    public async Task OnNavigatedToAsync_LoadsFirstPageAndGroupsByLocalDate()
    {
        var records = new[]
        {
            CreateSummary("today-a", LocalNoonUtc()),
            CreateSummary("today-b", LocalNoonUtc().AddHours(-1)),
            CreateSummary("older", LocalNoonUtc(-2))
        };
        SetupPages(records);
        var sut = CreateSut();

        await sut.OnNavigatedToAsync();

        sut.Items.Should().HaveCount(3);
        sut.Groups.Should().HaveCount(2);
        sut.Groups[0].Date.Should().BeAfter(sut.Groups[1].Date);
        sut.TotalCount.Should().Be(3);
        sut.HasMore.Should().BeFalse();
        sut.IsEmpty.Should().BeFalse();
        sut.HasNoMatches.Should().BeFalse();
        sut.IsLoading.Should().BeFalse();
        sut.ResultsSummary.Should().Be("3 results");
        sut.ShowingSummary.Should().Be("Showing 3 / 3");
    }

    [Fact]
    public async Task LoadMoreCommand_AppendsNextPageWithoutDuplicatesAndStopsAtTotal()
    {
        var records = Enumerable.Range(0, 61)
            .Select(index => CreateSummary($"board-{index}", DateTime.UtcNow.AddMinutes(-index)))
            .ToList();
        SetupPages(records);
        var sut = CreateSut();
        await sut.LoadFirstPageAsync();

        sut.Items.Should().HaveCount(60);
        sut.HasMore.Should().BeTrue();

        await sut.LoadMoreCommand.ExecuteAsync(null);

        sut.Items.Should().HaveCount(61);
        sut.Items.Select(item => item.Id).Should().OnlyHaveUniqueItems();
        sut.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task LoadMoreCommand_WhenAllItemsLoaded_DoesNotQueryAgain()
    {
        SetupPages(new[] { CreateSummary("only", DateTime.UtcNow) });
        var sut = CreateSut();
        await sut.LoadFirstPageAsync();
        int calls = 0;
        SetupPages(new[] { CreateSummary("only", DateTime.UtcNow) }, _ => calls++);

        await sut.LoadMoreCommand.ExecuteAsync(null);

        calls.Should().Be(0);
    }

    [Fact]
    public async Task EmptyAndNoMatchStates_DistinguishMissingHistoryFromFilteredOutResults()
    {
        SetupPages(Array.Empty<InspectionSummary>());
        var sut = CreateSut();
        await sut.LoadFirstPageAsync();

        sut.IsEmpty.Should().BeTrue();
        sut.HasNoMatches.Should().BeFalse();

        sut.Filters.StatusOptions.Single(option => option.Value == InspectionStatus.Pass).IsSelected = true;
        await sut.LoadFirstPageAsync();

        sut.IsEmpty.Should().BeFalse();
        sut.HasNoMatches.Should().BeTrue();
    }

    [Fact]
    public async Task FilterChange_ReloadsFromPageOneWithFilterValues()
    {
        var records = new[] { CreateSummary("board", DateTime.UtcNow) };
        var filtersSeen = new List<InspectionQueryFilter>();
        var reloaded = new TaskCompletionSource();
        int calls = 0;
        _repository
            .Setup(repo => repo.GetSummariesPagedAsync(It.IsAny<InspectionQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InspectionQueryFilter filter, CancellationToken _) =>
            {
                lock (filtersSeen)
                {
                    filtersSeen.Add(filter);
                }

                if (Interlocked.Increment(ref calls) == 2)
                {
                    reloaded.TrySetResult();
                }

                return new PagedResult<InspectionSummary>(records, 1, filter.PageSize, records.Length);
            });

        var sut = CreateSut();
        await sut.LoadFirstPageAsync();
        sut.SelectLoadedCommand.Execute(null);
        sut.SelectedCount.Should().Be(1);

        sut.Filters.SelectedPreset = HistoryDatePreset.Day;

        await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => !sut.IsLoading);

        var latest = filtersSeen[^1];
        latest.PageNumber.Should().Be(1);
        latest.StartDateUtc.Should().NotBeNull();
        sut.SelectedCount.Should().Be(0);
    }

    [Fact]
    public async Task SelectLoadedAndClearSelection_UpdateSelectedCount()
    {
        SetupPages(new[]
        {
            CreateSummary("a", DateTime.UtcNow),
            CreateSummary("b", DateTime.UtcNow.AddMinutes(-1))
        });
        var sut = CreateSut();
        await sut.LoadFirstPageAsync();

        sut.SelectLoadedCommand.Execute(null);
        sut.SelectedCount.Should().Be(2);

        sut.ClearSelectionCommand.Execute(null);
        sut.SelectedCount.Should().Be(0);
    }

    [Fact]
    public async Task DeleteSelected_WhenConfirmationCancelled_DoesNotDelete()
    {
        _confirmationService
            .Setup(service => service.ConfirmAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        SetupPages(new[] { CreateSummary("a", DateTime.UtcNow) });
        var sut = CreateSut();
        await sut.LoadFirstPageAsync();
        sut.SelectLoadedCommand.Execute(null);

        await sut.DeleteSelectedCommand.ExecuteAsync(null);

        _deletionService.Verify(
            service => service.DeleteManyAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        sut.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task DeleteSelected_WhenConfirmed_RemovesItemsUpdatesTotalAndRaisesEvent()
    {
        var records = new[]
        {
            CreateSummary("a", DateTime.UtcNow),
            CreateSummary("b", DateTime.UtcNow.AddMinutes(-1))
        };
        SetupPages(records);
        _deletionService
            .Setup(service => service.DeleteManyAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InspectionDeletionResult(2, 3, Array.Empty<string>()));
        var sut = CreateSut();
        await sut.LoadFirstPageAsync();
        sut.SelectLoadedCommand.Execute(null);
        IReadOnlyList<Guid>? deletedIds = null;
        sut.RecordsDeleted += (_, ids) => deletedIds = ids;

        await sut.DeleteSelectedCommand.ExecuteAsync(null);

        sut.Items.Should().BeEmpty();
        sut.TotalCount.Should().Be(0);
        sut.SelectedCount.Should().Be(0);
        deletedIds.Should().BeEquivalentTo(records.Select(record => record.Id));
        _notificationService.Verify(
            service => service.ShowToastAsync(
                It.Is<string>(title => title.Contains("Deleted")),
                It.Is<string>(message => message.Contains("2")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteSelected_WhenMorePagesExist_ResetsPagingWithoutSkippingRecords()
    {
        var remaining = Enumerable.Range(0, 120)
            .Select(index => CreateSummary($"board-{index}", LocalNoonUtc().AddMinutes(-index)))
            .ToList();
        _repository
            .Setup(repo => repo.GetSummariesPagedAsync(It.IsAny<InspectionQueryFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InspectionQueryFilter filter, CancellationToken _) =>
            {
                var items = remaining
                    .Skip((filter.PageNumber - 1) * filter.PageSize)
                    .Take(filter.PageSize)
                    .ToList();
                return new PagedResult<InspectionSummary>(items, filter.PageNumber, filter.PageSize, remaining.Count);
            });
        _deletionService
            .Setup(service => service.DeleteManyAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
            {
                int count = remaining.RemoveAll(record => ids.Contains(record.Id));
                return new InspectionDeletionResult(count, 0, Array.Empty<string>());
            });
        var sut = CreateSut();
        await sut.LoadFirstPageAsync();
        sut.Items[0].IsSelected = true;

        await sut.DeleteSelectedCommand.ExecuteAsync(null);

        sut.Items.Should().HaveCount(60);
        sut.TotalCount.Should().Be(119);
        sut.HasMore.Should().BeTrue();

        await sut.LoadMoreCommand.ExecuteAsync(null);

        sut.Items.Should().HaveCount(119);
        sut.Items.Select(item => item.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task DeleteSelected_WhenDeletionFails_RetainsSelectionAndReportsError()
    {
        SetupPages(new[] { CreateSummary("a", DateTime.UtcNow) });
        _deletionService
            .Setup(service => service.DeleteManyAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("disk offline"));
        var sut = CreateSut();
        await sut.LoadFirstPageAsync();
        sut.SelectLoadedCommand.Execute(null);

        await sut.DeleteSelectedCommand.ExecuteAsync(null);

        sut.Items.Should().HaveCount(1);
        sut.SelectedCount.Should().Be(1);
        sut.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task LoadFirstPageAsync_WhenRepositoryFails_SetsErrorState()
    {
        _repository
            .Setup(repo => repo.GetSummariesPagedAsync(It.IsAny<InspectionQueryFilter>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database offline"));
        var sut = CreateSut();

        await sut.LoadFirstPageAsync();

        sut.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        sut.IsLoading.Should().BeFalse();
        sut.Items.Should().BeEmpty();
        sut.IsEmpty.Should().BeFalse();
        sut.HasNoMatches.Should().BeFalse();
    }

    [Fact]
    public async Task LoadFirstPageAsync_HydratesThumbnailsForLoadedItems()
    {
        SetupPages(new[]
        {
            CreateSummary("a", LocalNoonUtc()),
            CreateSummary("b", LocalNoonUtc().AddMinutes(-1))
        });
        int calls = 0;
        var hydrated = new TaskCompletionSource();
        _thumbnailService
            .Setup(service => service.GetThumbnailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken _) =>
            {
                if (Interlocked.Increment(ref calls) == 2)
                {
                    hydrated.TrySetResult();
                }

                return Task.FromResult<BitmapSource?>(null);
            });
        var sut = CreateSut();

        await sut.LoadFirstPageAsync();
        await hydrated.Task.WaitAsync(TimeSpan.FromSeconds(5));

        calls.Should().Be(2);
        sut.Items.Should().OnlyContain(item => item.IsImageUnavailable);
    }

    [Fact]
    public async Task OpenViewerAsync_LoadsRecordAndOpensOverlay()
    {
        var summary = CreateSummary("viewer-record", LocalNoonUtc());
        SetupPages(new[] { summary });
        _repository
            .Setup(repo => repo.GetByIdAsync(summary.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InspectionRecord(
                summary.Id,
                summary.Name,
                summary.CreatedAtUtc,
                summary.UpdatedAtUtc,
                summary.Mode,
                summary.ImagePath));
        var sut = CreateSut();
        await sut.LoadFirstPageAsync();

        await sut.OpenViewerAsync(sut.Items.Single());

        sut.IsViewerOpen.Should().BeTrue();
        sut.Viewer.CurrentRecordId.Should().Be(summary.Id);
        sut.Viewer.CurrentName.Should().Be(summary.Name);
    }

    [Fact]
    public async Task ViewerDeletion_RemovesCardAndClosesOverlay()
    {
        var summary = CreateSummary("deleted-from-viewer", LocalNoonUtc());
        SetupPages(new[] { summary });
        string imagePath = Path.Combine(Path.GetTempPath(), $"pcb-viewer-{Guid.NewGuid():N}.png");
        using (var mat = new Mat(80, 120, MatType.CV_8UC3, Scalar.All(160)))
        {
            Cv2.ImWrite(imagePath, mat).Should().BeTrue();
        }

        try
        {
            _repository
                .Setup(repo => repo.GetByIdAsync(summary.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new InspectionRecord(
                    summary.Id,
                    summary.Name,
                    summary.CreatedAtUtc,
                    summary.UpdatedAtUtc,
                    summary.Mode,
                    imagePath));
            var sut = CreateSut();
            await sut.LoadFirstPageAsync();
            await sut.OpenViewerAsync(sut.Items.Single());

            await sut.Viewer.DeleteImageCommand.ExecuteAsync(null);

            sut.Items.Should().BeEmpty();
            sut.TotalCount.Should().Be(0);
            sut.IsViewerOpen.Should().BeFalse();
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    [Fact]
    public async Task OnNavigatedFromAsync_CancelsInFlightLoad()
    {
        _repository
            .Setup(repo => repo.GetSummariesPagedAsync(It.IsAny<InspectionQueryFilter>(), It.IsAny<CancellationToken>()))
            .Returns(async (InspectionQueryFilter _, CancellationToken token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                return PagedResult<InspectionSummary>.Empty(60);
            });
        var sut = CreateSut();

        var loadTask = sut.OnNavigatedToAsync();
        await sut.OnNavigatedFromAsync();
        await loadTask.WaitAsync(TimeSpan.FromSeconds(5));

        sut.Items.Should().BeEmpty();
        sut.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task RenameFailure_RaisesNotification()
    {
        SetupPages(new[] { CreateSummary("a", DateTime.UtcNow) });
        _repository
            .Setup(repo => repo.UpdateNameAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage offline"));
        var sut = CreateSut();
        await sut.LoadFirstPageAsync();

        var item = sut.Items.Single();
        item.BeginRename();
        item.EditedName = "new-name";
        await item.CommitRenameAsync();
        await WaitUntilAsync(() => _notificationService.Invocations.Count > 0);

        _notificationService.Verify(
            service => service.ShowToastAsync(
                It.Is<string>(title => title.Contains("Rename")),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
