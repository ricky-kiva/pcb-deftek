using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Export;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;
using pcb_deftek.Infrastructure.Vision;
using Xunit;

namespace pcb_deftek.Tests;

public class DashboardPdfExportServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"pcb-deftek-export-tests-{Guid.NewGuid():N}");
    private readonly string _reportsDirectory;
    private readonly string _fallbackDirectory;
    private readonly string _stagingRoot;
    private readonly FakeDashboardRepository _repository = new();
    private readonly FakeUserSettingsService _settings = new();
    private readonly FakeAnnotationService _annotation = new();
    private readonly FakeDeletionService _deletion = new();
    private readonly FakeNotificationService _notification = new();
    private readonly RecordingProgress _progress = new();
    private readonly FixedTimeProvider _time = new(
        new DateTimeOffset(2026, 11, 15, 10, 30, 0, TimeSpan.Zero));

    public DashboardPdfExportServiceTests()
    {
        _reportsDirectory = Path.Combine(_root, "reports");
        _fallbackDirectory = Path.Combine(_root, "fallback");
        _stagingRoot = Path.Combine(_root, "staging");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_stagingRoot);
        _settings.Settings = new UserSettings { ReportExportPath = _reportsDirectory };
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task ExportAsync_WhenNoInspections_ReturnsNoMatchesWithoutProgressOrFile()
    {
        _repository.MatchingInspections = Array.Empty<InspectionRecord>();
        var sut = CreateSut();

        DashboardExportResult result = await sut.ExportAsync(DashboardFilter.AllTime, _progress);

        result.Outcome.Should().Be(DashboardExportOutcome.NoMatches);
        result.HasFile.Should().BeFalse();
        _progress.Values.Should().BeEmpty();
        _settings.LoadCount.Should().Be(0);
        _repository.StatisticsCalls.Should().Be(0);
        Directory.Exists(_reportsDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task ExportAsync_StagesAnnotatedPngsWritesPdfAndNotifies()
    {
        var records = new[]
        {
            CreateRecord(Path.Combine(_root, "a.png")),
            CreateRecord(Path.Combine(_root, "b.png"))
        };
        _repository.MatchingInspections = records;
        _repository.Statistics = CreateStatistics(2, 1);
        var sut = CreateSut();

        DashboardExportResult result = await sut.ExportAsync(DashboardFilter.AllTime, _progress);

        result.Outcome.Should().Be(DashboardExportOutcome.Exported);
        result.MatchingInspections.Should().Be(2);
        result.FilePath.Should().NotBeNull();
        File.Exists(result.FilePath).Should().BeTrue();
        File.ReadAllBytes(result.FilePath!).Take(5).Should().Equal(Encoding.ASCII.GetBytes("%PDF-"));
        new FileInfo(result.FilePath!).Length.Should().BeGreaterThan(1_000);
        _annotation.MaxDimension.Should().Be(1600);
        _notification.Messages.Should().ContainSingle(message => message.Title == "Report exported");
        _progress.Values.Select(value => value.Percentage).Should().ContainInOrder(
            0, 10, 15, 52.5, 90, 95, 100);
        Directory.GetFiles(_stagingRoot).Should().BeEmpty();
    }

    [Fact]
    public async Task ExportAsync_BlankSettingsPath_UsesFallbackDirectoryAndCreatesIt()
    {
        _settings.Settings = new UserSettings { ReportExportPath = "   " };
        _repository.MatchingInspections = new[] { CreateRecord(Path.Combine(_root, "a.png")) };
        _repository.Statistics = CreateStatistics(1, 0);
        var sut = CreateSut();

        DashboardExportResult result = await sut.ExportAsync(DashboardFilter.AllTime);

        Directory.Exists(_fallbackDirectory).Should().BeTrue();
        result.FilePath.Should().StartWith(
            Path.GetFullPath(_fallbackDirectory) + Path.DirectorySeparatorChar);
    }

    [Fact]
    public void FileNamePolicy_UsesTimestampAndCollisionSuffixes()
    {
        var local = new DateTime(2026, 11, 15, 10, 30, 0, DateTimeKind.Utc);

        DashboardPdfExportService.BuildFileName(local, 0)
            .Should().Be("PCB-Deftek-Report-20261115-103000.pdf");
        DashboardPdfExportService.BuildFileName(local, 2)
            .Should().Be("PCB-Deftek-Report-20261115-103000 (2).pdf");

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(_reportsDirectory, "PCB-Deftek-Report-20261115-103000.pdf"),
            Path.Combine(_reportsDirectory, "PCB-Deftek-Report-20261115-103000 (1).pdf")
        };

        DashboardPdfExportService.FindAvailablePath(
                _reportsDirectory,
                local,
                existing.Contains)
            .Should().Be(Path.Combine(
                _reportsDirectory,
                "PCB-Deftek-Report-20261115-103000 (2).pdf"));
    }

    [Fact]
    public async Task ExportAsync_WhenTimestampNameExists_UsesCollisionSuffix()
    {
        Directory.CreateDirectory(_reportsDirectory);
        File.WriteAllText(
            Path.Combine(_reportsDirectory, "PCB-Deftek-Report-20261115-103000.pdf"),
            "existing");
        File.WriteAllText(
            Path.Combine(_reportsDirectory, "PCB-Deftek-Report-20261115-103000 (1).pdf"),
            "existing");
        _repository.MatchingInspections = new[] { CreateRecord(Path.Combine(_root, "a.png")) };
        _repository.Statistics = CreateStatistics(1, 0);
        var sut = CreateSut();

        DashboardExportResult result = await sut.ExportAsync(DashboardFilter.AllTime);

        result.FilePath.Should().EndWith("PCB-Deftek-Report-20261115-103000 (2).pdf");
        File.Exists(result.FilePath).Should().BeTrue();
    }

    [Fact]
    public async Task ExportAsync_WhenCancelled_ThrowsCleansArtifactsAndDoesNotNotify()
    {
        _repository.MatchingInspections = new[] { CreateRecord(Path.Combine(_root, "a.png")) };
        _repository.Statistics = CreateStatistics(1, 0);
        var blocked = new ManualResetEventSlim();
        _annotation.Block = blocked;
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();

        Task<DashboardExportResult> export = sut.ExportAsync(
            DashboardFilter.AllTime,
            _progress,
            cts.Token);
        await WaitForAnnotationStartAsync();
        cts.Cancel();
        blocked.Set();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export);
        _notification.Messages.Should().BeEmpty();
        Directory.Exists(_reportsDirectory).Should().BeTrue();
        Directory.GetFiles(_reportsDirectory, "*.pdf").Should().BeEmpty();
        Directory.GetFiles(_reportsDirectory, "*.tmp").Should().BeEmpty();
        Directory.GetDirectories(_stagingRoot).Should().BeEmpty();
    }

    [Fact]
    public async Task ExportAsync_WhenAnnotationThrows_WrapsFailureCleansAndNotifies()
    {
        _repository.MatchingInspections = new[] { CreateRecord(Path.Combine(_root, "a.png")) };
        _repository.Statistics = CreateStatistics(1, 0);
        _annotation.Exception = new InvalidOperationException("annotation failed");
        var sut = CreateSut();

        await Assert.ThrowsAsync<ExportException>(
            () => sut.ExportAsync(DashboardFilter.AllTime, _progress));

        _notification.Messages.Should().ContainSingle(message => message.Title == "Report export failed");
        Directory.GetFiles(_reportsDirectory, "*.pdf").Should().BeEmpty();
        Directory.GetDirectories(_stagingRoot).Should().BeEmpty();
    }

    [Fact]
    public async Task ExportAsync_WhenAnotherExportIsRunning_RejectsConcurrentCall()
    {
        _repository.MatchingInspections = new[] { CreateRecord(Path.Combine(_root, "a.png")) };
        _repository.Statistics = CreateStatistics(1, 0);
        var blocked = new ManualResetEventSlim();
        _annotation.Block = blocked;
        var sut = CreateSut();

        Task<DashboardExportResult> first = sut.ExportAsync(DashboardFilter.AllTime);
        await WaitForAnnotationStartAsync();

        await Assert.ThrowsAsync<ExportException>(
            () => sut.ExportAsync(DashboardFilter.AllTime));

        blocked.Set();
        await first;
    }

    [Fact]
    public async Task ExportAsync_InvalidImage_DeletesRecordExcludesItAndContinues()
    {
        string invalidPath = Path.Combine(_root, "corrupt.png");
        InspectionRecord valid = CreateRecord(Path.Combine(_root, "valid.png"));
        InspectionRecord invalid = CreateRecord(invalidPath, fail: true);
        _repository.MatchingInspections = new[] { valid, invalid };
        _repository.Statistics = CreateStatistics(2, 1);
        _annotation.ResultsByPath[invalidPath] = null;
        var sut = CreateSut();

        DashboardExportResult result = await sut.ExportAsync(DashboardFilter.AllTime);

        result.Outcome.Should().Be(DashboardExportOutcome.Exported);
        result.MatchingInspections.Should().Be(1);
        _deletion.DeletedIds.Should().ContainSingle().Which.Should().Be(invalid.Id);
        _annotation.CallsByPath[invalidPath].Should().Be(2);
        _notification.Messages.Should().ContainSingle(message => message.Title == "Report exported");
    }

    [Fact]
    public async Task ExportAsync_InvalidImageWithUnavailableParent_SkipsDeletionAndContinues()
    {
        string invalidPath = Path.Combine(_root, "missing-parent", "corrupt.png");
        InspectionRecord valid = CreateRecord(Path.Combine(_root, "valid.png"));
        InspectionRecord invalid = CreateRecord(invalidPath, fail: true);
        _repository.MatchingInspections = new[] { valid, invalid };
        _repository.Statistics = CreateStatistics(2, 1);
        _annotation.ResultsByPath[invalidPath] = null;
        var sut = CreateSut();

        DashboardExportResult result = await sut.ExportAsync(DashboardFilter.AllTime);

        result.Outcome.Should().Be(DashboardExportOutcome.Exported);
        _deletion.DeletedIds.Should().BeEmpty();
    }

    [Fact]
    public async Task ExportAsync_DeletionFailure_IsRetriedOnceAndDoesNotAbortExport()
    {
        string invalidPath = Path.Combine(_root, "corrupt.png");
        InspectionRecord valid = CreateRecord(Path.Combine(_root, "valid.png"));
        InspectionRecord invalid = CreateRecord(invalidPath, fail: true);
        _repository.MatchingInspections = new[] { valid, invalid };
        _repository.Statistics = CreateStatistics(2, 1);
        _annotation.ResultsByPath[invalidPath] = null;
        int attempts = 0;
        _deletion.Handler = (id, _) =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new IOException("locked");
            }

            return Task.FromResult(new InspectionDeletionResult(1, 0, Array.Empty<string>()));
        };
        var sut = CreateSut();

        DashboardExportResult result = await sut.ExportAsync(DashboardFilter.AllTime);

        result.Outcome.Should().Be(DashboardExportOutcome.Exported);
        attempts.Should().Be(2);
        _deletion.DeletedIds.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExportAsync_WhenAllImagesAreUnreadable_ThrowsAndWritesNoPdf()
    {
        string path = Path.Combine(_root, "corrupt.png");
        InspectionRecord invalid = CreateRecord(path, fail: true);
        _repository.MatchingInspections = new[] { invalid };
        _repository.Statistics = CreateStatistics(1, 1);
        _annotation.ResultsByPath[path] = null;
        var sut = CreateSut();

        await Assert.ThrowsAsync<ExportException>(
            () => sut.ExportAsync(DashboardFilter.AllTime));

        _deletion.DeletedIds.Should().ContainSingle().Which.Should().Be(invalid.Id);
        Directory.GetFiles(_reportsDirectory, "*.pdf").Should().BeEmpty();
        _notification.Messages.Should().ContainSingle(message => message.Title == "Report export failed");
    }

    [Fact]
    public async Task Dispose_CancelsInFlightExportAndRejectsNewCalls()
    {
        var waitForCancellation = new TaskCompletionSource<IReadOnlyList<InspectionRecord>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _repository.EvidenceHandler = async (_, token) =>
        {
            await waitForCancellation.Task.WaitAsync(token);
            return Array.Empty<InspectionRecord>();
        };
        var sut = CreateSut();

        Task<DashboardExportResult> export = sut.ExportAsync(DashboardFilter.AllTime);
        sut.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => sut.ExportAsync(DashboardFilter.AllTime));
        waitForCancellation.TrySetResult(Array.Empty<InspectionRecord>());
    }

    [Fact]
    public async Task CountMatchingInspectionsAsync_DelegatesToRepository()
    {
        _repository.Count = 7;
        var sut = CreateSut();

        int count = await sut.CountMatchingInspectionsAsync(DashboardFilter.AllTime);

        count.Should().Be(7);
        _repository.CountCalls.Should().Be(1);
    }

    private async Task WaitForAnnotationStartAsync()
    {
        bool started = await Task.Run(() => _annotation.Started.Wait(TimeSpan.FromSeconds(5)));
        started.Should().BeTrue("the annotation service should have been invoked");
    }

    private DashboardPdfExportService CreateSut() => new(
        _repository,
        _settings,
        _annotation,
        _deletion,
        _notification,
        NullLogger<DashboardPdfExportService>.Instance,
        _time,
        _stagingRoot,
        _fallbackDirectory);

    private static InspectionRecord CreateRecord(string imagePath, bool fail = false)
    {
        Guid id = Guid.NewGuid();
        var defects = fail
            ? new[]
            {
                new DetectedDefect(
                    Guid.NewGuid(),
                    id,
                    DefectKnowledgeBase.GetInfo(DefectClass.Open).Code,
                    0.91f,
                    new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f))
            }
            : Array.Empty<DetectedDefect>();

        DateTime now = new(2026, 11, 15, 10, 30, 0, DateTimeKind.Utc);
        return new InspectionRecord(
            id,
            "Board",
            now,
            now,
            DetectionMode.SingleFile,
            imagePath,
            defects);
    }

    private static DashboardStatistics CreateStatistics(int inspections, int defects)
    {
        var bucket = new DashboardBucket(
            new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Local),
            new DateTime(2026, 11, 15, 23, 59, 59, DateTimeKind.Local),
            "15 Nov",
            inspections,
            defects,
            inspections - defects,
            defects);
        var classes = DefectKnowledgeBase.GetAll()
            .OrderBy(info => info.ClassId)
            .Select((info, index) => new DashboardClassCount(
                info.Class,
                info.Abbreviation,
                index == 0 ? defects : 0))
            .ToArray();
        return new DashboardStatistics(DashboardBucketGranularity.Day, new[] { bucket }, classes);
    }

    private static BitmapSource CreateFrozenBitmap()
    {
        const int width = 8;
        const int height = 8;
        int stride = width * 3;
        var pixels = new byte[stride * height];
        for (int i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = 30;
            pixels[i + 1] = 60;
            pixels[i + 2] = 90;
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgr24,
            null,
            pixels,
            stride);
        bitmap.Freeze();
        return bitmap;
    }

    private sealed class RecordingProgress : IProgress<DashboardExportProgress>
    {
        public List<DashboardExportProgress> Values { get; } = new();

        public void Report(DashboardExportProgress value) => Values.Add(value);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FakeUserSettingsService : IUserSettingsService
    {
        public UserSettings Settings { get; set; } = new();

        public int LoadCount { get; private set; }

        public Task<UserSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(Settings);
        }

        public Task SaveSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeDashboardRepository : IDashboardRepository
    {
        public IReadOnlyList<InspectionRecord> MatchingInspections { get; set; } =
            Array.Empty<InspectionRecord>();

        public DashboardStatistics Statistics { get; set; } = DashboardStatistics.Empty;

        public int Count { get; set; }

        public int StatisticsCalls { get; private set; }

        public int CountCalls { get; private set; }

        public Func<DashboardFilter, CancellationToken, Task<IReadOnlyList<InspectionRecord>>>? EvidenceHandler { get; set; }

        public Task<DashboardStatistics> GetStatisticsAsync(
            DashboardFilter filter,
            CancellationToken cancellationToken = default)
        {
            StatisticsCalls++;
            return Task.FromResult(Statistics);
        }

        public Task<IReadOnlyList<InspectionRecord>> GetMatchingInspectionsAsync(
            DashboardFilter filter,
            CancellationToken cancellationToken = default)
        {
            return EvidenceHandler is null
                ? Task.FromResult(MatchingInspections)
                : EvidenceHandler(filter, cancellationToken);
        }

        public Task<int> GetMatchingInspectionCountAsync(
            DashboardFilter filter,
            CancellationToken cancellationToken = default)
        {
            CountCalls++;
            return Task.FromResult(Count);
        }
    }

    private sealed class FakeAnnotationService : IImageAnnotationService
    {
        public Dictionary<string, BitmapSource?> ResultsByPath { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> CallsByPath { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public ManualResetEventSlim? Block { get; set; }

        public ManualResetEventSlim Started { get; } = new();

        public Exception? Exception { get; set; }

        public int? MaxDimension { get; private set; }

        public BitmapSource? AnnotateImage(
            string imagePath,
            IReadOnlyList<DetectedDefect> defects,
            int maxDimension = 0)
        {
            Started.Set();
            MaxDimension = maxDimension;
            CallsByPath[imagePath] = CallsByPath.GetValueOrDefault(imagePath) + 1;

            if (Exception is not null)
            {
                throw Exception;
            }

            Block?.Wait();

            return ResultsByPath.TryGetValue(imagePath, out BitmapSource? result)
                ? result
                : CreateFrozenBitmap();
        }

        public BitmapSource? AnnotateMat(Mat sourceMat, IReadOnlyList<DetectedDefect> defects, int maxDimension = 0) =>
            throw new NotSupportedException();

        public BitmapSource? MatToBitmapSource(Mat sourceMat) =>
            throw new NotSupportedException();
    }

    private sealed class FakeDeletionService : IInspectionDeletionService
    {
        public List<Guid> DeletedIds { get; } = new();

        public Func<Guid, CancellationToken, Task<InspectionDeletionResult>>? Handler { get; set; }

        public Task<InspectionDeletionResult> DeleteAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            DeletedIds.Add(id);
            return Handler is null
                ? Task.FromResult(new InspectionDeletionResult(1, 0, Array.Empty<string>()))
                : Handler(id, cancellationToken);
        }

        public Task<InspectionDeletionResult> DeleteManyAsync(
            IReadOnlyCollection<Guid> ids,
            CancellationToken cancellationToken = default)
        {
            DeletedIds.AddRange(ids);
            return Task.FromResult(new InspectionDeletionResult(ids.Count, 0, Array.Empty<string>()));
        }
    }

    private sealed class FakeNotificationService : INotificationService
    {
        public List<(string Title, string Message)> Messages { get; } = new();

        public Task ShowToastAsync(
            string title,
            string message,
            CancellationToken cancellationToken = default)
        {
            Messages.Add((title, message));
            return Task.CompletedTask;
        }
    }
}
