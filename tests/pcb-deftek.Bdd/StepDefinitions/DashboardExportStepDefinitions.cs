using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using pcb_deftek.Bdd.Support;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Export;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence;
using pcb_deftek.Infrastructure.Persistence.Migrations;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;
using pcb_deftek.Infrastructure.Vision;
using pcb_deftek.Presentation.Services;
using pcb_deftek.Presentation.ViewModels;
using Reqnroll;

namespace pcb_deftek.Bdd.StepDefinitions;

[Binding]
public class DashboardExportStepDefinitions : IDisposable
{
    private readonly string _testRoot;
    private readonly string _reportsDirectory;
    private readonly string _defaultReportsDirectory;
    private readonly string _stagingRoot;
    private readonly string _imageRoot;
    private readonly SqliteInspectionRepository _inspectionRepository;
    private readonly DashboardProgressService _progress = new();
    private readonly RecordingNotificationService _notifications = new();
    private readonly RecordingConfirmationService _confirmation = new();
    private readonly MutableSettingsService _settings;
    private readonly BlockingAnnotationService _annotation = new();
    private readonly DashboardPdfExportService _exportService;
    private readonly DashboardViewModel _dashboard;
    private readonly Dictionary<string, InspectionRecord> _records = new(StringComparer.Ordinal);

    public DashboardExportStepDefinitions()
    {
        _testRoot = Path.Combine(
            Path.GetTempPath(),
            $"pcb-deftek-bdd-export-{Guid.NewGuid():N}");
        _reportsDirectory = Path.Combine(_testRoot, "reports");
        _defaultReportsDirectory = Path.Combine(
            _testRoot,
            "Downloads",
            "PCB Deftek",
            "Report");
        _stagingRoot = Path.Combine(_testRoot, "staging");
        _imageRoot = Path.Combine(_testRoot, "images");
        Directory.CreateDirectory(_testRoot);
        Directory.CreateDirectory(_stagingRoot);
        Directory.CreateDirectory(_imageRoot);

        var factory = new TestSqliteConnectionFactory(Path.Combine(_testRoot, "export.db"));
        new SqliteDatabaseMigrator(factory).MigrateToLatestAsync().GetAwaiter().GetResult();
        _inspectionRepository = new SqliteInspectionRepository(factory);
        var dashboardRepository = new SqliteDashboardRepository(factory);

        _settings = new MutableSettingsService(new UserSettings
        {
            ReportExportPath = _reportsDirectory,
            ImageOutputDirectory = _imageRoot
        });

        var deletionService = new InspectionDeletionService(
            _inspectionRepository,
            NullLogger<InspectionDeletionService>.Instance,
            capturesDirectoryPath: null,
            imageOutputDirectory: _imageRoot);
        QuestPdfConfiguration.EnsureConfigured();

        _exportService = new DashboardPdfExportService(
            dashboardRepository,
            _settings,
            _annotation,
            deletionService,
            _notifications,
            NullLogger<DashboardPdfExportService>.Instance,
            TimeProvider.System,
            _stagingRoot,
            _defaultReportsDirectory);

        _dashboard = new DashboardViewModel(
            dashboardRepository,
            _settings,
            _progress,
            _exportService,
            _confirmation,
            NullLogger<DashboardViewModel>.Instance,
            _notifications);
    }

    public void Dispose()
    {
        _annotation.Release.Set();
        _exportService.Dispose();
        SqliteConnection.ClearAllPools();

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

    [Given(@"an isolated export database")]
    public void GivenAnIsolatedExportDatabase() => Directory.Exists(_testRoot).Should().BeTrue();

    [Given(@"the report export path is configured")]
    public void GivenTheReportExportPathIsConfigured() =>
        _settings.Settings = _settings.Settings with { ReportExportPath = _reportsDirectory };

    [Given(@"the report export path is blank")]
    public void GivenTheReportExportPathIsBlank() =>
        _settings.Settings = _settings.Settings with { ReportExportPath = "   " };

    [Given(@"an exportable inspection ""(.*)"" with an? ""(.*)"" defect")]
    public Task GivenAnExportableInspectionWithDefect(string name, string defectClass)
    {
        return InsertRecordAsync(
            name,
            CreateImage(name),
            new[] { DefectFromCode(defectClass) });
    }

    [Given(@"no exportable inspections")]
    public void GivenNoExportableInspections()
    {
        // The isolated database starts empty.
    }

    [Given(@"101 exportable inspections")]
    public async Task Given101ExportableInspections()
    {
        string sharedImage = CreateImage("large-export");
        var defects = new[] { DefectFromCode("short") };

        for (int index = 1; index <= 101; index++)
        {
            await InsertRecordAsync($"large-board-{index:D3}", sharedImage, defects);
        }
    }

    [Given(@"an inspection ""(.*)"" whose image is missing")]
    public Task GivenAnInspectionWhoseImageIsMissing(string name)
    {
        string missingPath = Path.Combine(_imageRoot, $"{name}-missing.png");
        return InsertRecordAsync(name, missingPath, Array.Empty<DetectedDefect>());
    }

    [When(@"I filter to short defects and export")]
    public async Task WhenIFilterToShortDefectsAndExport()
    {
        _dashboard.Filters.ClassOptions
            .Single(option => option.Value == DefectClass.Short)
            .IsSelected = true;
        await _dashboard.LoadAsync();
        _dashboard.Statistics!.TotalInspections.Should().Be(1);
        await _dashboard.RequestExportCommand.ExecuteAsync(null);
    }

    [When(@"I export the dashboard report")]
    public async Task WhenIExportTheDashboardReport()
    {
        await _dashboard.RequestExportCommand.ExecuteAsync(null);
    }

    [When(@"I export and decline the large-report confirmation")]
    public async Task WhenIExportAndDeclineTheLargeReportConfirmation()
    {
        _confirmation.Result = false;
        await _dashboard.RequestExportCommand.ExecuteAsync(null);
    }

    [When(@"I start an export and cancel it from the Background Activity modal")]
    public async Task WhenIStartAnExportAndCancelItFromTheBackgroundActivityModal()
    {
        _annotation.BlockNextCall = true;
        Task exportTask = _dashboard.RequestExportCommand.ExecuteAsync(null);

        try
        {
            await WaitUntilAsync(
                () => _annotation.Started.IsSet && _progress.DataExport.IsRunning,
                TimeSpan.FromSeconds(30),
                () => $"the export should be running before cancellation (annotation started: {_annotation.Started.IsSet}, data export status: {_progress.DataExport.Status})");
            _dashboard.CancelProcessCommand.Execute(DashboardProcess.DataExport);
        }
        finally
        {
            _annotation.Release.Set();
        }

        await exportTask;
    }

    [Then(@"a report PDF should exist in the configured export directory")]
    public void ThenAReportPdfShouldExistInTheConfiguredExportDirectory()
    {
        string[] files = PdfFiles(_reportsDirectory);
        files.Should().ContainSingle();
        File.Exists(files[0]).Should().BeTrue();
    }

    [Then(@"a report PDF should exist in the default report directory")]
    public void ThenAReportPdfShouldExistInTheDefaultReportDirectory()
    {
        string[] files = PdfFiles(_defaultReportsDirectory);
        files.Should().ContainSingle();
        Path.GetFullPath(files[0]).Should().StartWith(
            Path.GetFullPath(_defaultReportsDirectory) + Path.DirectorySeparatorChar);
    }

    [Then(@"the report PDF should start with the PDF signature")]
    public void ThenTheReportPdfShouldStartWithThePdfSignature()
    {
        string file = PdfFiles(_defaultReportsDirectory).Single();
        Encoding.ASCII.GetString(File.ReadAllBytes(file).Take(5).ToArray())
            .Should()
            .Be("%PDF-");
    }

    [Then(@"the report notification should have fired")]
    public void ThenTheReportNotificationShouldHaveFired() =>
        _notifications.Snapshot.Should().Contain(message => message.Title == "Report exported");

    [Then(@"the export should complete at 100 percent")]
    public void ThenTheExportShouldCompleteAt100Percent()
    {
        _progress.DataExport.Status.Should().Be(DashboardProgressStatus.Completed);
        _progress.DataExport.Percentage.Should().Be(100);
    }

    [Then(@"no report PDF should be written")]
    public void ThenNoReportPdfShouldBeWritten()
    {
        PdfFiles(_reportsDirectory).Should().BeEmpty();
        PdfFiles(_defaultReportsDirectory).Should().BeEmpty();
    }

    [Then(@"the no-match notification should have fired")]
    public void ThenTheNoMatchNotificationShouldHaveFired() =>
        _notifications.Snapshot.Should().Contain(message => message.Title == "No matching inspections");

    [Then(@"the background activity modal should remain closed")]
    public void ThenTheBackgroundActivityModalShouldRemainClosed() =>
        _dashboard.IsNotificationModalOpen.Should().BeFalse();

    [Then(@"the confirmation should mention 101 inspections")]
    public void ThenTheConfirmationShouldMention101Inspections()
    {
        _confirmation.Requests.Should().ContainSingle()
            .Which.Message.Should().Contain("101");
    }

    [Then(@"the data export should be cancelled")]
    public void ThenTheDataExportShouldBeCancelled()
    {
        _progress.DataExport.Status.Should().Be(DashboardProgressStatus.Cancelled);
        _notifications.Snapshot.Should().NotContain(message => message.Title == "Report exported");
    }

    [Then(@"no export staging directory should remain")]
    public void ThenNoExportStagingDirectoryShouldRemain() =>
        Directory.GetDirectories(_stagingRoot).Should().BeEmpty();

    [Then(@"the broken inspection should be deleted")]
    public async Task ThenTheBrokenInspectionShouldBeDeleted()
    {
        var broken = _records["broken-board"];
        (await _inspectionRepository.GetByIdAsync(broken.Id)).Should().BeNull();
    }

    [Then(@"the readable inspection should remain")]
    public async Task ThenTheReadableInspectionShouldRemain()
    {
        var readable = _records["readable-board"];
        (await _inspectionRepository.GetByIdAsync(readable.Id)).Should().NotBeNull();
    }

    private async Task InsertRecordAsync(
        string name,
        string imagePath,
        IReadOnlyList<DetectedDefect> defects)
    {
        var id = Guid.NewGuid();
        var record = new InspectionRecord(
            id,
            name,
            DateTime.UtcNow,
            DateTime.UtcNow,
            DetectionMode.SingleFile,
            imagePath,
            defects.Select(defect => new DetectedDefect(
                Guid.NewGuid(),
                id,
                defect.ClassCode,
                defect.Confidence,
                defect.BoundingBox)).ToArray());

        await _inspectionRepository.InsertAsync(record);
        _records[name] = record;
    }

    private string CreateImage(string name)
    {
        string path = Path.Combine(_imageRoot, $"{name}-{Guid.NewGuid():N}.png");
        using var mat = new Mat(120, 160, MatType.CV_8UC3, Scalar.All(140));
        Cv2.ImWrite(path, mat).Should().BeTrue();
        return path;
    }

    private static DetectedDefect DefectFromCode(string code) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        DefectKnowledgeBase.GetByCode(code).Code,
        0.9f,
        new BoundingBox(0.1f, 0.1f, 0.25f, 0.25f));

    private static string[] PdfFiles(string directory) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.pdf", SearchOption.AllDirectories)
            : Array.Empty<string>();

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, Func<string>? reason = null)
    {
        DateTime deadline = DateTime.UtcNow.Add(timeout);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        condition().Should().BeTrue(reason?.Invoke() ?? "the expected background operation should start before the timeout");
    }

    private sealed class MutableSettingsService : IUserSettingsService
    {
        private UserSettings _settings;

        public MutableSettingsService(UserSettings settings) => _settings = settings;

        public UserSettings Settings
        {
            get => _settings;
            set => _settings = value;
        }

        public Task<UserSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_settings);

        public Task SaveSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        private readonly List<(string Title, string Message)> _messages = new();

        public IReadOnlyList<(string Title, string Message)> Snapshot
        {
            get
            {
                lock (_messages)
                {
                    return _messages.ToArray();
                }
            }
        }

        public Task ShowToastAsync(
            string title,
            string message,
            CancellationToken cancellationToken = default)
        {
            lock (_messages)
            {
                _messages.Add((title, message));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingConfirmationService : IConfirmationService
    {
        public bool Result { get; set; } = true;

        public List<(string Title, string Message)> Requests { get; } = new();

        public Task<bool> ConfirmAsync(
            string title,
            string message,
            string confirmText = "Delete",
            CancellationToken cancellationToken = default)
        {
            Requests.Add((title, message));
            return Task.FromResult(Result);
        }
    }

    private sealed class BlockingAnnotationService : IImageAnnotationService
    {
        private readonly ImageAnnotationService _inner = new();

        public bool BlockNextCall { get; set; }

        public ManualResetEventSlim Started { get; } = new(false);

        public ManualResetEventSlim Release { get; } = new(true);

        public BitmapSource? AnnotateImage(
            string imagePath,
            IReadOnlyList<DetectedDefect> defects,
            int maxDimension = 0)
        {
            if (BlockNextCall)
            {
                BlockNextCall = false;
                Started.Set();
                Release.Wait(TimeSpan.FromSeconds(10));
            }

            return _inner.AnnotateImage(imagePath, defects, maxDimension);
        }

        public BitmapSource? AnnotateMat(
            Mat sourceMat,
            IReadOnlyList<DetectedDefect> defects,
            int maxDimension = 0) =>
            _inner.AnnotateMat(sourceMat, defects, maxDimension);

        public BitmapSource? MatToBitmapSource(Mat sourceMat) =>
            _inner.MatToBitmapSource(sourceMat);
    }
}
