using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Export;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Presentation.Services;
using pcb_deftek.Presentation.ViewModels;
using Xunit;

namespace pcb_deftek.Tests;

public class DashboardReportContentFactoryTests
{
    [Fact]
    public void Create_BuildsFourCanonicalMetricsWithDashboardValues()
    {
        var statistics = SampleStatistics();

        var document = CreateDocument(statistics);

        document.Title.Should().Be("Defect Inspection Report");
        document.Subtitle.Should().Be("PCB Deftek");
        document.Metrics.Select(metric => metric.Key).Should().Equal(
            "pcbs-inspected",
            "defects-detected",
            "inspection-rate",
            "defect-distribution");
        document.Metrics.Select(metric => metric.Title).Should().Equal(
            "PCBs Inspected",
            "Defects Detected",
            "Inspection Rate",
            "Defect Distribution");
        document.Metrics[0].DisplayValue.Should().Be("4");
        document.Metrics[1].DisplayValue.Should().Be("3");
        document.Metrics[2].DisplayValue.Should().Be("1 PASS / 3 FAIL");
        document.Metrics[3].DisplayValue.Should().Be("3");
        document.Metrics[0].Caption.Should().Be("Daily · 2 buckets");
        document.Metrics[3].Caption.Should().Be("All nine defect classes");
        document.Metrics[0].SeriesColorHex.Should().Be("#0066FF");
        document.Metrics[1].SeriesColorHex.Should().Be("#E81123");
        document.Metrics[0].TimeSeries.Select(point => point.Value).Should().Equal(3, 1);
        document.Metrics[2].PassFailSeries.Select(point => (point.Passes, point.Fails))
            .Should().Equal((1, 2), (0, 1));
        document.Metrics[3].ClassCounts.Should().HaveCount(9);
        document.Metrics[3].ClassCounts.Single(item => item.Class == DefectClass.Short).Count.Should().Be(2);
    }

    [Fact]
    public async Task Create_MatchesDashboardViewModelProjections()
    {
        var statistics = SampleStatistics();
        var document = CreateDocument(statistics);
        var viewModel = new DashboardViewModel(
            new FakeDashboardRepository(statistics),
            new FakeSettingsService(),
            new DashboardProgressService());

        await viewModel.LoadAsync();

        viewModel.MetricCards.Should().HaveCount(4);
        foreach (DashboardReportMetric metric in document.Metrics)
        {
            DashboardMetricCardViewModel card = viewModel.MetricCards.Single(item => item.Title == metric.Title);
            metric.DisplayValue.Should().Be(card.DisplayValue);
            metric.Caption.Should().Be(card.Caption);
            metric.TimeSeries.Select(point => (point.Label, point.Value))
                .Should().Equal(card.TimeSeries.Select(point => (point.Label, point.Value)));
            metric.PassFailSeries.Select(point => (point.Label, point.Passes, point.Fails))
                .Should().Equal(card.PassFailSeries.Select(point => (point.Label, point.Passes, point.Fails)));
            metric.ClassCounts.Select(item => (item.Class, item.Count))
                .Should().Equal(card.ClassCounts.Select(item => (item.Class, item.Count)));
        }
    }

    [Fact]
    public void Create_BuildsRestrictedFilterSummaryInCanonicalOrder()
    {
        DateTime startLocal = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Local);
        DateTime endLocal = new(2026, 3, 5, 23, 59, 59, DateTimeKind.Local);
        var filter = new DashboardFilter(
            startDateUtc: startLocal.ToUniversalTime(),
            endDateUtc: endLocal.ToUniversalTime(),
            defectClasses: new[] { DefectClass.Open, DefectClass.Short },
            severities: new[] { DefectSeverity.MediumHigh, DefectSeverity.Critical });

        var document = CreateDocument(SampleStatistics(), filter: filter);

        document.FilterSummary.DateRange.Should().Be("2026-03-01 – 2026-03-05");
        document.FilterSummary.DefectClasses.Should().Be("Short, Open");
        document.FilterSummary.Severities.Should().Be("Critical, Medium-high");
    }

    [Fact]
    public void Create_UnrestrictedFilterSummaryUsesCanonicalText()
    {
        var allTime = CreateDocument(SampleStatistics(), filter: DashboardFilter.AllTime).FilterSummary;

        allTime.DateRange.Should().Be("All time");
        allTime.DefectClasses.Should().Be("All nine classes");
        allTime.Severities.Should().Be("All severities");

        var allClasses = CreateDocument(
            SampleStatistics(),
            filter: new DashboardFilter(defectClasses: DefectKnowledgeBase.GetAll().Select(info => info.Class).ToArray()))
            .FilterSummary;

        allClasses.DefectClasses.Should().Be("All nine classes");
        allClasses.Severities.Should().Be("All severities");
    }

    [Fact]
    public void Create_MapsEvidenceAndSortsDefectsDeterministically()
    {
        var statistics = SampleStatistics();
        var passId = Guid.NewGuid();
        var failId = Guid.NewGuid();
        DateTime failLocal = new(2026, 3, 2, 10, 0, 0, DateTimeKind.Local);
        DateTime passLocal = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Local);
        var pass = new InspectionRecord(
            passId,
            "Pass board",
            passLocal.ToUniversalTime(),
            passLocal.ToUniversalTime(),
            DetectionMode.SingleFile,
            "images/pass.png",
            Array.Empty<DetectedDefect>());
        var fail = new InspectionRecord(
            failId,
            "Fail board",
            failLocal.ToUniversalTime(),
            failLocal.ToUniversalTime(),
            DetectionMode.LiveCamera,
            "images/fail.png",
            new[]
            {
                Defect(failId, DefectClass.Short, 0.75f),
                Defect(failId, DefectClass.Open, 0.92f)
            },
            InspectionOrigin.LiveCapture);
        var staged = new Dictionary<Guid, string>
        {
            [passId] = "staged/pass.png",
            [failId] = "staged/fail.png"
        };

        var document = DashboardReportContentFactory.Create(
            statistics,
            new[] { pass, fail },
            staged,
            DashboardFilter.AllTime,
            new DateTime(2026, 3, 3, 9, 30, 0, DateTimeKind.Local),
            CultureInfo.GetCultureInfo("en-US"));

        document.Inspections.Select(inspection => inspection.Id).Should().Equal(passId, failId);
        DashboardReportInspection passBlock = document.Inspections[0];
        passBlock.Name.Should().Be("Pass board");
        passBlock.CreatedAtLocal.Should().Be(passLocal);
        passBlock.ModeLabel.Should().Be("Single file");
        passBlock.StatusLabel.Should().Be("PASS");
        passBlock.StagedImagePath.Should().Be(Path.GetFullPath("staged/pass.png"));
        passBlock.Defects.Should().BeEmpty();

        DashboardReportInspection failBlock = document.Inspections[1];
        failBlock.ModeLabel.Should().Be("Live camera");
        failBlock.StatusLabel.Should().Be("FAIL");
        failBlock.DefectCount.Should().Be(2);
        failBlock.Defects.Select(defect => defect.ClassName).Should().Equal("Open", "Short");
        failBlock.Defects.Select(defect => defect.Confidence).Should().Equal(0.92f, 0.75f);
        failBlock.Defects.Select(defect => defect.Severity).Should()
            .Equal(DefectKnowledgeBase.GetInfo(DefectClass.Open).Severity, DefectKnowledgeBase.GetInfo(DefectClass.Short).Severity);
    }

    [Fact]
    public void Create_RequiresAStagedPathForEveryInspection()
    {
        var record = new InspectionRecord(
            Guid.NewGuid(),
            "Board",
            DateTime.UtcNow,
            DateTime.UtcNow,
            DetectionMode.SingleFile,
            "images/board.png",
            Array.Empty<DetectedDefect>());

        Action act = () => DashboardReportContentFactory.Create(
            SampleStatistics(),
            new[] { record },
            new Dictionary<Guid, string>(),
            DashboardFilter.AllTime,
            DateTime.Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_AllowsNoEvidenceForTheOrchestratorToReject()
    {
        var document = DashboardReportContentFactory.Create(
            SampleStatistics(),
            Array.Empty<InspectionRecord>(),
            new Dictionary<Guid, string>(),
            DashboardFilter.AllTime,
            new DateTime(2026, 3, 3, 9, 30, 0, DateTimeKind.Local));

        document.Inspections.Should().BeEmpty();
        document.Metrics.Should().HaveCount(4);
    }

    private static DashboardReportDocument CreateDocument(
        DashboardStatistics statistics,
        DashboardFilter? filter = null) =>
        DashboardReportContentFactory.Create(
            statistics,
            Array.Empty<InspectionRecord>(),
            new Dictionary<Guid, string>(),
            filter ?? DashboardFilter.AllTime,
            new DateTime(2026, 3, 3, 9, 30, 0, DateTimeKind.Local),
            CultureInfo.GetCultureInfo("en-US"));

    private static DetectedDefect Defect(Guid inspectionId, DefectClass defectClass, float confidence) =>
        new(
            Guid.NewGuid(),
            inspectionId,
            DefectKnowledgeBase.GetInfo(defectClass).Code,
            confidence,
            new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f));

    private static DashboardStatistics SampleStatistics()
    {
        var buckets = new[]
        {
            new DashboardBucket(
                new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Local),
                new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Local),
                "1 Mar",
                3,
                2,
                1,
                2),
            new DashboardBucket(
                new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Local),
                new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Local),
                "2 Mar",
                1,
                1,
                0,
                1)
        };
        var classes = DefectKnowledgeBase.GetAll()
            .OrderBy(info => info.ClassId)
            .Select(info => new DashboardClassCount(
                info.Class,
                info.Abbreviation,
                info.Class == DefectClass.Short ? 2 : info.Class == DefectClass.Open ? 1 : 0))
            .ToArray();
        return new DashboardStatistics(DashboardBucketGranularity.Day, buckets, classes);
    }

    private sealed class FakeDashboardRepository : IDashboardRepository
    {
        private readonly DashboardStatistics _statistics;

        public FakeDashboardRepository(DashboardStatistics statistics) => _statistics = statistics;

        public Task<DashboardStatistics> GetStatisticsAsync(
            DashboardFilter filter,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_statistics);
    }

    private sealed class FakeSettingsService : IUserSettingsService
    {
        private UserSettings _settings = new();

        public Task<UserSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_settings);

        public Task SaveSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
    }
}
