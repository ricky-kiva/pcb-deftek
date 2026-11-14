using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence;
using pcb_deftek.Infrastructure.Persistence.Migrations;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;
using pcb_deftek.Presentation.Services;
using pcb_deftek.Presentation.ViewModels;
using pcb_deftek.Presentation.Models;
using Reqnroll;

namespace pcb_deftek.Bdd.StepDefinitions;

[Binding]
public class HistoryAndViewerStepDefinitions : IDisposable
{
    private readonly string _testRoot;
    private readonly string _imageRoot;
    private readonly string _capturesRoot;
    private readonly SqliteInspectionRepository _repository;
    private readonly InspectionViewerViewModel _viewer;
    private readonly HistoryViewModel _history;
    private readonly ConfirmationStub _confirmation = new();
    private readonly Dictionary<string, InspectionRecord> _records = new(StringComparer.Ordinal);
    private InspectionRecord? _currentRecord;
    private string? _cropPath;
    private string? _annotatedCapturePath;

    public HistoryAndViewerStepDefinitions()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"pcb-deftek-bdd-history-{Guid.NewGuid():N}");
        string dbPath = Path.Combine(_testRoot, "history.db");
        _imageRoot = Path.Combine(_testRoot, "Images");
        _capturesRoot = Path.Combine(_testRoot, "Captures");
        Directory.CreateDirectory(_testRoot);
        Directory.CreateDirectory(_imageRoot);
        Directory.CreateDirectory(_capturesRoot);

        var factory = new SqliteConnectionFactory(dbPath);
        new SqliteDatabaseMigrator(factory).MigrateToLatestAsync().GetAwaiter().GetResult();
        _repository = new SqliteInspectionRepository(factory);

        var notifications = new NotificationStub();
        var settings = new SettingsServiceStub(_imageRoot);

        var cropService = new ViewportCropService(
            NullLogger<ViewportCropService>.Instance,
            settings);
        var deletionService = new InspectionDeletionService(
            _repository,
            NullLogger<InspectionDeletionService>.Instance,
            _capturesRoot,
            _imageRoot);

        _viewer = new InspectionViewerViewModel(
            _repository,
            cropService,
            deletionService,
            _confirmation,
            notifications,
            NullLogger<InspectionViewerViewModel>.Instance);

        _history = new HistoryViewModel(
            _repository,
            new HistoryThumbnailService(),
            notifications,
            deletionService,
            _confirmation,
            _viewer,
            NullLogger<HistoryViewModel>.Instance);
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

    [Given(@"an isolated history database")]
    public void GivenAnIsolatedHistoryDatabase() => Directory.Exists(_testRoot).Should().BeTrue();

    [Given(@"the history contains a passing inspection today and a failing inspection yesterday")]
    public async Task GivenPassingTodayAndFailingYesterday()
    {
        await InsertRecord("pass-board", LocalNoonUtc(), CreateImage("pass-board"), Array.Empty<DetectedDefect>());
        await InsertRecord("fail-board", LocalNoonUtc(-1), CreateImage("fail-board"), new[] { Defect("fail-board", DefectClass.Short, 0.9f) });
    }

    [Given(@"a history record named ""(.*)""")]
    public async Task GivenAHistoryRecord(string name)
    {
        _currentRecord = await InsertRecord(name, LocalNoonUtc(), CreateImage(name), Array.Empty<DetectedDefect>());
    }

    [Given(@"a history record ""(.*)"" with ""(.*)"" and ""(.*)"" defects")]
    public async Task GivenAHistoryRecordWithTwoDefects(string name, string firstClass, string secondClass)
    {
        _currentRecord = await InsertRecord(
            name,
            LocalNoonUtc(),
            CreateImage(name),
            new[]
            {
                DefectFromCode(name, firstClass, 0.9f),
                DefectFromCode(name, secondClass, 0.7f)
            });
    }

    [Given(@"a history record ""(.*)"" with a ""(.*)"" defect")]
    public async Task GivenAHistoryRecordWithDefect(string name, string defectClass)
    {
        _currentRecord = await InsertRecord(
            name,
            LocalNoonUtc(),
            CreateImage(name),
            new[] { DefectFromCode(name, defectClass, 0.9f) });
    }

    [Given(@"a 200x100 inspection image named ""(.*)""")]
    public async Task GivenA200x100InspectionImage(string name)
    {
        _currentRecord = await InsertRecord(name, LocalNoonUtc(), CreateImage(name, 200, 100), Array.Empty<DetectedDefect>());
    }

    [Given(@"an inspection ""(.*)"" whose image file is missing")]
    public async Task GivenAnInspectionWithMissingImage(string name)
    {
        string missingPath = Path.Combine(_imageRoot, $"{name}-missing.png");
        _currentRecord = await InsertRecord(name, LocalNoonUtc(), missingPath, Array.Empty<DetectedDefect>());
    }

    [Given(@"a mixed history for filtering")]
    public async Task GivenAMixedHistoryForFiltering()
    {
        await InsertRecord("today-short", LocalNoonUtc(), CreateImage("today-short"), new[] { Defect("today-short", DefectClass.Short, 0.9f) });
        await InsertRecord("today-pass", LocalNoonUtc(), CreateImage("today-pass"), Array.Empty<DetectedDefect>());
        await InsertRecord("old-open", LocalNoonUtc(-1), CreateImage("old-open"), new[] { Defect("old-open", DefectClass.Open, 0.8f) });
    }

    [Given(@"a live capture ""(.*)"" and an external import ""(.*)""")]
    public async Task GivenLiveAndExternalRecords(string liveName, string externalName)
    {
        string imageId = Guid.NewGuid().ToString("N");
        string rawPath = Path.Combine(_imageRoot, $"{imageId}_raw.png");
        string annotatedPath = Path.Combine(_imageRoot, $"{imageId}_annotated.png");
        _annotatedCapturePath = annotatedPath;
        File.WriteAllBytes(rawPath, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(annotatedPath, new byte[] { 4, 5, 6 });
        _records[liveName] = await InsertRecord(
            liveName,
            LocalNoonUtc(),
            rawPath,
            new[] { Defect(liveName, DefectClass.Open, 0.8f) },
            DetectionMode.LiveCamera,
            InspectionOrigin.LiveCapture);

        string externalPath = Path.Combine(_testRoot, $"{externalName}-original.png");
        File.WriteAllBytes(externalPath, new byte[] { 7, 8, 9 });
        _records[externalName] = await InsertRecord(
            externalName,
            LocalNoonUtc(),
            externalPath,
            Array.Empty<DetectedDefect>(),
            DetectionMode.SingleFile,
            InspectionOrigin.External);
    }

    [Given(@"a live capture ""(.*)"" selected for deletion")]
    public async Task GivenALiveCaptureSelectedForDeletion(string name)
    {
        string rawPath = Path.Combine(_imageRoot, $"{name}-raw.png");
        File.WriteAllBytes(rawPath, new byte[] { 1 });
        _records[name] = await InsertRecord(
            name,
            LocalNoonUtc(),
            rawPath,
            Array.Empty<DetectedDefect>(),
            DetectionMode.LiveCamera,
            InspectionOrigin.LiveCapture);
        await LoadHistory();
        _records[name].Id.Should().NotBeEmpty();
        _history.Items.Single(item => item.Id == _records[name].Id).IsSelected = true;
    }

    [Given(@"an open viewer for a deletable crop image")]
    public async Task GivenAnOpenViewerForADeletableCropImage()
    {
        string imagePath = CreateImage("crop-delete", 200, 100);
        _currentRecord = await InsertRecord(
            "crop-delete",
            LocalNoonUtc(),
            imagePath,
            Array.Empty<DetectedDefect>(),
            DetectionMode.SingleFile,
            InspectionOrigin.Crop);
        await LoadHistory();
        await _history.OpenViewerAsync(_history.Items.Single(item => item.Id == _currentRecord.Id));
    }

    [When(@"the history page loads")]
    public async Task WhenTheHistoryPageLoads() => await LoadHistory();

    [When(@"I rename the record to ""(.*)"" and reload")]
    public async Task WhenIRenameAndReload(string newName)
    {
        await LoadHistory();
        var item = _history.Items.Single();
        item.BeginRename();
        item.EditedName = newName;
        await item.CommitRenameAsync();
        await LoadHistory();
    }

    [When(@"I open the viewer for ""(.*)""")]
    public async Task WhenIOpenTheViewerFor(string name)
    {
        await LoadHistory();
        var item = _history.Items.Single(card => card.Name == name);
        await _history.OpenViewerAsync(item);
    }

    [When(@"I focus the first defect marker")]
    public void WhenIFocusTheFirstDefectMarker() => _viewer.SelectMarker(_viewer.DefectMarkers[0]);

    [When(@"I crop the normalized region (.*), (.*), (.*), (.*)")]
    public async Task WhenICropNormalizedRegion(double x, double y, double width, double height)
    {
        await _viewer.CropViewportCommand.ExecuteAsync(new NormalizedRect(x, y, width, height));
        await WaitUntilAsync(() => Directory.GetFiles(_imageRoot, "*_crop_*.png").Length > 0);
        _cropPath = Directory.GetFiles(_imageRoot, "*_crop_*.png").Single();
    }

    [When(@"I filter to today, FAIL, and short defects")]
    public async Task WhenIFilterToTodayFailAndShort()
    {
        await LoadHistory();
        _history.Filters.SelectedPreset = DateRangePreset.Day;
        _history.Filters.StatusOptions.Single(option => option.Value == InspectionStatus.Fail).IsSelected = true;
        _history.Filters.ClassOptions.Single(option => option.Value == DefectClass.Short).IsSelected = true;
        await _history.LoadFirstPageAsync();
        await WaitUntilAsync(() => !_history.IsLoading && _history.TotalCount == 1);
    }

    [When(@"I select both records and confirm deletion")]
    public async Task WhenISelectBothAndConfirmDeletion()
    {
        _confirmation.Result = true;
        await LoadHistory();
        foreach (var item in _history.Items)
        {
            item.IsSelected = true;
        }

        await _history.DeleteSelectedCommand.ExecuteAsync(null);
    }

    [When(@"I cancel deletion")]
    public async Task WhenICancelDeletion()
    {
        _confirmation.Result = false;
        await _history.DeleteSelectedCommand.ExecuteAsync(null);
    }

    [When(@"I confirm deletion from the viewer sidebar")]
    public async Task WhenIConfirmDeletionFromViewer()
    {
        _confirmation.Result = true;
        await _viewer.DeleteImageCommand.ExecuteAsync(null);
    }

    [Then(@"the gallery should contain 2 date groups")]
    public void ThenTheGalleryShouldContainTwoDateGroups() => _history.Groups.Should().HaveCount(2);

    [Then(@"the cards should report one pass and one fail")]
    public void ThenCardsReportOnePassAndOneFail()
    {
        _history.Items.Count(item => item.IsPass).Should().Be(1);
        _history.Items.Count(item => !item.IsPass).Should().Be(1);
    }

    [Then(@"the history card name should be ""(.*)""")]
    public void ThenTheHistoryCardNameShouldBe(string name) => _history.Items.Single().Name.Should().Be(name);

    [Then(@"the viewer default sidebar should show the record name and timestamp")]
    public void ThenViewerDefaultSidebarMetadata()
    {
        _viewer.CurrentName.Should().NotBeNullOrWhiteSpace();
        _viewer.FormattedTimestamp.Should().NotBeNullOrWhiteSpace();
    }

    [Then(@"the viewer should show (.*) defect class summaries")]
    public void ThenViewerShowsClassSummaries(int count) => _viewer.ClassSummaries.Should().HaveCount(count);

    [Then(@"the focused sidebar should show the short diagnostic fields")]
    public void ThenFocusedSidebarShowsShortDiagnostics()
    {
        _viewer.IsDefectFocused.Should().BeTrue();
        _viewer.FocusedClassName.Should().Be("Short");
        _viewer.FocusedSeverityText.Should().Be("Critical");
        _viewer.FocusedRootCause.Should().NotBeNullOrWhiteSpace();
        _viewer.FocusedImpact.Should().NotBeNullOrWhiteSpace();
        _viewer.FocusedAction.Should().NotBeNullOrWhiteSpace();
    }

    [Then(@"the crop PNG should be (.*)x(.*)")]
    public void ThenCropDimensions(int width, int height)
    {
        _cropPath.Should().NotBeNull();
        File.Exists(_cropPath).Should().BeTrue();
        using var crop = Cv2.ImRead(_cropPath!, ImreadModes.Unchanged);
        crop.Width.Should().Be(width);
        crop.Height.Should().Be(height);
    }

    [Then(@"the gallery should contain the cropped inspection")]
    public async Task ThenGalleryContainsCroppedInspection()
    {
        await WaitUntilAsync(() => _history.Items.Any(item => item.Origin == InspectionOrigin.Crop));
        _history.Items.Should().Contain(item => item.Origin == InspectionOrigin.Crop);
    }

    [Then(@"the viewer should show the unavailable state")]
    public void ThenViewerShowsUnavailable()
    {
        _viewer.IsImageUnavailable.Should().BeTrue();
        _viewer.ImageSource.Should().BeNull();
    }

    [Then(@"only the failing short record should remain")]
    public void ThenOnlyFailingShortRemains()
    {
        _history.TotalCount.Should().Be(1);
        _history.Items.Should().ContainSingle();
        _history.Items[0].Name.Should().Be("today-short");
    }

    [Then(@"the live capture files and both rows should be deleted")]
    public async Task ThenLiveCaptureAndBothRowsDeleted()
    {
        var live = _records["capture-board"];
        var external = _records["external-board"];
        File.Exists(live.ImagePath).Should().BeFalse();
        File.Exists(_annotatedCapturePath).Should().BeFalse();
        (await _repository.GetByIdAsync(live.Id)).Should().BeNull();
        (await _repository.GetByIdAsync(external.Id)).Should().BeNull();
        _history.Items.Should().BeEmpty();
    }

    [Then(@"the external original should remain on disk")]
    public void ThenExternalOriginalRemains()
    {
        var external = _records["external-board"];
        File.Exists(external.ImagePath).Should().BeTrue();
    }

    [Then(@"the live capture row and files should remain")]
    public async Task ThenLiveCaptureRemains()
    {
        var record = _records["cancel-board"];
        File.Exists(record.ImagePath).Should().BeTrue();
        (await _repository.GetByIdAsync(record.Id)).Should().NotBeNull();
    }

    [Then(@"the card should remain selected")]
    public void ThenCardRemainsSelected() => _history.Items.Single().IsSelected.Should().BeTrue();

    [Then(@"the viewer should be closed")]
    public void ThenViewerShouldBeClosed()
    {
        _history.IsViewerOpen.Should().BeFalse();
        _viewer.CurrentRecordId.Should().BeNull();
    }

    [Then(@"the gallery should no longer contain the deleted crop")]
    public void ThenGalleryNoLongerContainsCrop()
    {
        _history.Items.Should().BeEmpty();
        _viewer.CurrentRecordId.Should().BeNull();
    }

    private async Task LoadHistory()
    {
        await _history.LoadFirstPageAsync();
        await WaitUntilAsync(() => !_history.IsLoading && _history.Items.Count == _history.TotalCount);
    }

    private async Task<InspectionRecord> InsertRecord(
        string name,
        DateTime createdAtUtc,
        string imagePath,
        IReadOnlyList<DetectedDefect> defects,
        DetectionMode mode = DetectionMode.SingleFile,
        InspectionOrigin origin = InspectionOrigin.External)
    {
        var id = Guid.NewGuid();
        var mapped = defects.Select(defect => new DetectedDefect(
            Guid.NewGuid(),
            id,
            defect.ClassCode,
            defect.Confidence,
            defect.BoundingBox)).ToArray();
        var record = new InspectionRecord(
            id,
            name,
            createdAtUtc,
            createdAtUtc,
            mode,
            imagePath,
            mapped,
            origin);
        await _repository.InsertAsync(record);
        _records[name] = record;
        _currentRecord = record;
        return record;
    }

    private string CreateImage(string name, int width = 160, int height = 120)
    {
        string path = Path.Combine(_imageRoot, $"{name}-{Guid.NewGuid():N}.png");
        using var mat = new Mat(height, width, MatType.CV_8UC3, Scalar.All(140));
        Cv2.ImWrite(path, mat).Should().BeTrue();
        return path;
    }

    private static DetectedDefect Defect(string name, DefectClass defectClass, float confidence)
    {
        var inspectionId = Guid.NewGuid();
        return new DetectedDefect(
            Guid.NewGuid(),
            inspectionId,
            DefectKnowledgeBase.GetInfo(defectClass).Code,
            confidence,
            new BoundingBox(0.1f, 0.1f, 0.25f, 0.25f));
    }

    private static DetectedDefect DefectFromCode(string name, string code, float confidence) =>
        Defect(name, DefectKnowledgeBase.GetByCode(code).Class, confidence);

    private static DateTime LocalNoonUtc(int dayOffset = 0)
    {
        var localNoon = DateTime.SpecifyKind(
            DateTime.Today.AddDays(dayOffset).AddHours(12),
            DateTimeKind.Local);
        return localNoon.ToUniversalTime();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    private sealed class ConfirmationStub : IConfirmationService
    {
        public bool Result { get; set; } = true;

        public Task<bool> ConfirmAsync(
            string title,
            string message,
            string confirmText = "Delete",
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);
    }

    private sealed class NotificationStub : INotificationService
    {
        public Task ShowToastAsync(string title, string message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SettingsServiceStub : IUserSettingsService
    {
        private readonly string _imageOutputDirectory;

        public SettingsServiceStub(string imageOutputDirectory)
        {
            _imageOutputDirectory = imageOutputDirectory;
        }

        public Task<UserSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserSettings { ImageOutputDirectory = _imageOutputDirectory });

        public Task SaveSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
