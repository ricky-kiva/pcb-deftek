using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Export;
using Xunit;

namespace pcb_deftek.Tests;

public class DashboardExportContractTests
{
    [Fact]
    public void Exported_RequiresAtLeastOneMatchingInspection()
    {
        Action act = () => DashboardExportResult.Exported("report.pdf", 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Exported_RequiresAFilePath()
    {
        Action act = () => DashboardExportResult.Exported("  ", 1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Exported_NormalizesTheFilePath()
    {
        DashboardExportResult result = DashboardExportResult.Exported("reports/report.pdf", 3);

        result.Outcome.Should().Be(DashboardExportOutcome.Exported);
        result.FilePath.Should().Be(Path.GetFullPath("reports/report.pdf"));
        result.MatchingInspections.Should().Be(3);
        result.HasFile.Should().BeTrue();
    }

    [Fact]
    public void NoMatches_CarriesNoFilePathOrCount()
    {
        DashboardExportResult result = DashboardExportResult.NoMatches();

        result.Outcome.Should().Be(DashboardExportOutcome.NoMatches);
        result.FilePath.Should().BeNull();
        result.MatchingInspections.Should().Be(0);
        result.HasFile.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(42.5, 42.5)]
    [InlineData(100.0, 100.0)]
    [InlineData(150.0, 100.0)]
    public void Progress_ClampsPercentageToTheValidRange(double input, double expected)
    {
        var progress = new DashboardExportProgress(input, "Loading");

        progress.Percentage.Should().Be(expected);
    }

    [Fact]
    public void Progress_RejectsNonFinitePercentagesAndNormalizesMissingMessages()
    {
        Action act = () => new DashboardExportProgress(double.NaN, null);

        act.Should().Throw<ArgumentOutOfRangeException>();
        new DashboardExportProgress(10, null).Message.Should().BeEmpty();
        new DashboardExportProgress(10, "  Rendering  ").Message.Should().Be("Rendering");
    }

    [Fact]
    public void TimeSeriesMetric_KeepsOnlyItsOwnSeriesAndCopiesTheInput()
    {
        var points = new List<DashboardReportTimeSeriesPoint> { new("Mon", 4) };

        DashboardReportMetric metric = DashboardReportMetric.CreateTimeSeries(
            "pcbs",
            "PCBs Inspected",
            "4",
            "Daily · 1 bucket",
            points);
        points.Add(new DashboardReportTimeSeriesPoint("Tue", 9));

        metric.Kind.Should().Be(DashboardReportMetricKind.TimeSeries);
        metric.TimeSeries.Should().ContainSingle().Which.Value.Should().Be(4);
        metric.PassFailSeries.Should().BeEmpty();
        metric.ClassCounts.Should().BeEmpty();
    }

    [Fact]
    public void PassFailAndDistributionFactories_KeepTheirOwnPayloads()
    {
        DashboardReportMetric passFail = DashboardReportMetric.CreatePassFail(
            "rate",
            "Inspection Rate",
            "1 PASS / 1 FAIL",
            "Daily · 1 bucket",
            new[] { new DashboardReportPassFailPoint("Mon", 1, 1) });
        DashboardReportMetric distribution = DashboardReportMetric.CreateDistribution(
            "distribution",
            "Defect Distribution",
            "2",
            "All nine defect classes",
            NineClassCounts());

        passFail.PassFailSeries.Should().ContainSingle();
        passFail.TimeSeries.Should().BeEmpty();
        distribution.ClassCounts.Should().HaveCount(9);
        distribution.ClassCounts.Select(count => count.Class).Should().Equal(
            DefectKnowledgeBase.GetAll().OrderBy(info => info.ClassId).Select(info => info.Class));
    }

    [Fact]
    public void Metric_RejectsNegativeSeriesValuesAndEmptyIdentity()
    {
        Action negative = () => DashboardReportMetric.CreateTimeSeries(
            "pcbs",
            "PCBs",
            "1",
            "Daily",
            new[] { new DashboardReportTimeSeriesPoint("Mon", -1) });
        Action emptyKey = () => DashboardReportMetric.CreateTimeSeries(
            " ",
            "PCBs",
            "1",
            "Daily",
            Array.Empty<DashboardReportTimeSeriesPoint>());

        negative.Should().Throw<ArgumentOutOfRangeException>();
        emptyKey.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Defect_RequiresIdentityAndNormalizedConfidence()
    {
        Action missingClass = () => new DashboardReportDefect("  ", 0.5f, DefectSeverity.High);
        Action outOfRange = () => new DashboardReportDefect("Short", 1.01f, DefectSeverity.High);

        missingClass.Should().Throw<ArgumentException>();
        outOfRange.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Inspection_EnforcesPassFailConsistencyAndCopiesDefects()
    {
        var defects = new List<DashboardReportDefect> { new("Short", 0.9f, DefectSeverity.High) };
        var inspection = new DashboardReportInspection(
            Guid.NewGuid(),
            "Board A",
            DateTime.Today,
            DetectionMode.SingleFile,
            InspectionStatus.Fail,
            "staged/board-a.png",
            defects);
        defects.Clear();

        inspection.DefectCount.Should().Be(1);
        inspection.Defects.Should().ContainSingle();
        Action passWithDefects = () => new DashboardReportInspection(
            Guid.NewGuid(),
            "Board B",
            DateTime.Today,
            DetectionMode.SingleFile,
            InspectionStatus.Pass,
            "staged/board-b.png",
            new[] { new DashboardReportDefect("Short", 0.9f, DefectSeverity.High) });
        Action failWithoutDefects = () => new DashboardReportInspection(
            Guid.NewGuid(),
            "Board C",
            DateTime.Today,
            DetectionMode.SingleFile,
            InspectionStatus.Fail,
            "staged/board-c.png",
            Array.Empty<DashboardReportDefect>());

        passWithDefects.Should().Throw<ArgumentException>();
        failWithoutDefects.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Document_RequiresFourUniqueMetricsAndCopiesInspections()
    {
        var inspections = new List<DashboardReportInspection>
        {
            new(
                Guid.NewGuid(),
                "Board A",
                DateTime.Today,
                DetectionMode.LiveCamera,
                InspectionStatus.Pass,
                "staged/board-a.png",
                Array.Empty<DashboardReportDefect>())
        };
        var document = new DashboardReportDocument(
            "Defect Inspection Report",
            "PCB Deftek",
            DateTime.Today,
            new DashboardReportFilterSummary("All time", "All nine classes", "All severities"),
            CreateMetrics(),
            inspections);
        inspections.Clear();

        document.Inspections.Should().ContainSingle();
        Action threeMetrics = () => new DashboardReportDocument(
            "Report",
            "PCB Deftek",
            DateTime.Today,
            new DashboardReportFilterSummary("All time", "All nine classes", "All severities"),
            CreateMetrics().Take(3).ToArray(),
            inspections);
        Action duplicateKeys = () => new DashboardReportDocument(
            "Report",
            "PCB Deftek",
            DateTime.Today,
            new DashboardReportFilterSummary("All time", "All nine classes", "All severities"),
            CreateMetrics().Select(metric => DashboardReportMetric.CreateTimeSeries(
                "pcbs",
                metric.Title,
                metric.DisplayValue,
                metric.Caption,
                metric.TimeSeries)).ToArray(),
            inspections);

        threeMetrics.Should().Throw<ArgumentException>();
        duplicateKeys.Should().Throw<ArgumentException>();
    }

    private static IReadOnlyList<DashboardReportMetric> CreateMetrics() =>
        new[]
        {
            DashboardReportMetric.CreateTimeSeries(
                "pcbs",
                "PCBs Inspected",
                "4",
                "Daily · 1 bucket",
                new[] { new DashboardReportTimeSeriesPoint("Mon", 4) }),
            DashboardReportMetric.CreateTimeSeries(
                "defects",
                "Defects Detected",
                "2",
                "Daily · 1 bucket",
                new[] { new DashboardReportTimeSeriesPoint("Mon", 2) }),
            DashboardReportMetric.CreatePassFail(
                "rate",
                "Inspection Rate",
                "3 PASS / 1 FAIL",
                "Daily · 1 bucket",
                new[] { new DashboardReportPassFailPoint("Mon", 3, 1) }),
            DashboardReportMetric.CreateDistribution(
                "distribution",
                "Defect Distribution",
                "2",
                "All nine defect classes",
                NineClassCounts())
        };

    private static IReadOnlyList<DashboardClassCount> NineClassCounts() =>
        DefectKnowledgeBase.GetAll()
            .OrderBy(info => info.ClassId)
            .Select(info => new DashboardClassCount(info.Class, info.Abbreviation, 0))
            .ToArray();
}
