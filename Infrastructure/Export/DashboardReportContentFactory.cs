using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Infrastructure.Export;

/// <summary>
/// Builds the immutable report content from dashboard aggregates, matching inspections, and the
/// filter snapshot. Pure: no I/O, WPF, or QuestPDF types.
/// </summary>
public static class DashboardReportContentFactory
{
    /// <summary>Report title.</summary>
    public const string ReportTitle = "Defect Inspection Report";

    /// <summary>Report subtitle.</summary>
    public const string ReportSubtitle = "PCB Deftek";

    private const string TotalPcbsKey = "pcbs-inspected";
    private const string TotalDefectsKey = "defects-detected";
    private const string InspectionRateKey = "inspection-rate";
    private const string DistributionKey = "defect-distribution";
    private const string TotalPcbsColor = "#0066FF";
    private const string TotalDefectsColor = "#E81123";
    private const string DistributionCaption = "All nine defect classes";
    private const string AllClassesLabel = "All nine classes";
    private const string AllSeveritiesLabel = "All severities";
    private const string AllTimeLabel = "All time";

    /// <summary>
    /// Creates the report content model.
    /// </summary>
    /// <param name="statistics">Filtered dashboard aggregates.</param>
    /// <param name="inspections">Matching inspections, newest first.</param>
    /// <param name="stagedImagePaths">Annotated PNG path per included inspection; every inspection must have one.</param>
    /// <param name="filter">Filter snapshot rendered in the header.</param>
    /// <param name="generatedAtLocal">Local generation timestamp.</param>
    /// <param name="culture">Formatting culture; defaults to <see cref="CultureInfo.CurrentCulture"/>.</param>
    /// <returns>The immutable report document.</returns>
    public static DashboardReportDocument Create(
        DashboardStatistics statistics,
        IReadOnlyList<InspectionRecord> inspections,
        IReadOnlyDictionary<Guid, string> stagedImagePaths,
        DashboardFilter filter,
        DateTime generatedAtLocal,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        ArgumentNullException.ThrowIfNull(inspections);
        ArgumentNullException.ThrowIfNull(stagedImagePaths);
        ArgumentNullException.ThrowIfNull(filter);

        CultureInfo formattingCulture = culture ?? CultureInfo.CurrentCulture;
        string caption = $"{DescribeGranularity(statistics.BucketGranularity)} · {statistics.Buckets.Count} buckets";
        var metrics = new[]
        {
            DashboardReportMetric.CreateTimeSeries(
                TotalPcbsKey,
                "PCBs Inspected",
                statistics.TotalInspections.ToString("N0", formattingCulture),
                caption,
                statistics.Buckets
                    .Select(bucket => new DashboardReportTimeSeriesPoint(bucket.Label, bucket.Inspections))
                    .ToArray(),
                TotalPcbsColor),
            DashboardReportMetric.CreateTimeSeries(
                TotalDefectsKey,
                "Defects Detected",
                statistics.TotalDefects.ToString("N0", formattingCulture),
                caption,
                statistics.Buckets
                    .Select(bucket => new DashboardReportTimeSeriesPoint(bucket.Label, bucket.Defects))
                    .ToArray(),
                TotalDefectsColor),
            DashboardReportMetric.CreatePassFail(
                InspectionRateKey,
                "Inspection Rate",
                $"{statistics.PassCount.ToString("N0", formattingCulture)} PASS / {statistics.FailCount.ToString("N0", formattingCulture)} FAIL",
                caption,
                statistics.Buckets
                    .Select(bucket => new DashboardReportPassFailPoint(bucket.Label, bucket.Passes, bucket.Fails))
                    .ToArray()),
            DashboardReportMetric.CreateDistribution(
                DistributionKey,
                "Defect Distribution",
                statistics.TotalDefects.ToString("N0", formattingCulture),
                DistributionCaption,
                statistics.ClassCounts)
        };

        var evidence = inspections
            .Select(record => MapInspection(record, stagedImagePaths))
            .ToArray();

        return new DashboardReportDocument(
            ReportTitle,
            ReportSubtitle,
            generatedAtLocal,
            BuildFilterSummary(filter, formattingCulture),
            metrics,
            evidence);
    }

    private static DashboardReportInspection MapInspection(
        InspectionRecord record,
        IReadOnlyDictionary<Guid, string> stagedImagePaths)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!stagedImagePaths.TryGetValue(record.Id, out string? stagedImagePath) ||
            string.IsNullOrWhiteSpace(stagedImagePath))
        {
            throw new ArgumentException(
                $"Inspection '{record.Id}' has no staged image path.",
                nameof(stagedImagePaths));
        }

        DashboardReportDefect[] defects = record.Defects
            .OrderByDescending(defect => defect.Confidence)
            .ThenBy(defect => defect.ClassInfo.ClassId)
            .Select(defect => new DashboardReportDefect(
                defect.ClassName,
                defect.Confidence,
                defect.Severity))
            .ToArray();

        return new DashboardReportInspection(
            record.Id,
            record.Name,
            record.CreatedAtUtc.ToLocalTime(),
            record.Mode,
            record.Status,
            stagedImagePath,
            defects);
    }

    private static DashboardReportFilterSummary BuildFilterSummary(
        DashboardFilter filter,
        CultureInfo culture)
    {
        var distinctClasses = filter.DefectClasses.Distinct().ToArray();
        string classes = distinctClasses.Length is 0 or 9
            ? AllClassesLabel
            : string.Join(
                ", ",
                distinctClasses
                    .OrderBy(item => DefectKnowledgeBase.GetInfo(item).ClassId)
                    .Select(item => DefectKnowledgeBase.GetInfo(item).DisplayName));

        var distinctSeverities = filter.Severities.Distinct().ToArray();
        string severities = distinctSeverities.Length is 0 or 5
            ? AllSeveritiesLabel
            : string.Join(
                ", ",
                distinctSeverities
                    .OrderByDescending(item => item)
                    .Select(GetSeverityLabel));

        return new DashboardReportFilterSummary(
            FormatDateRange(filter, culture),
            classes,
            severities);
    }

    private static string FormatDateRange(DashboardFilter filter, CultureInfo culture)
    {
        if (filter.StartDateUtc is null && filter.EndDateUtc is null)
        {
            return AllTimeLabel;
        }

        if (filter.StartDateUtc is { } start && filter.EndDateUtc is { } end)
        {
            return $"{start.ToLocalTime().ToString("yyyy-MM-dd", culture)} – {end.ToLocalTime().ToString("yyyy-MM-dd", culture)}";
        }

        if (filter.StartDateUtc is { } startOnly)
        {
            return $"From {startOnly.ToLocalTime().ToString("yyyy-MM-dd", culture)}";
        }

        return $"Until {filter.EndDateUtc!.Value.ToLocalTime().ToString("yyyy-MM-dd", culture)}";
    }

    private static string GetSeverityLabel(DefectSeverity severity) => severity switch
    {
        DefectSeverity.Critical => "Critical",
        DefectSeverity.High => "High",
        DefectSeverity.MediumHigh => "Medium-high",
        DefectSeverity.Medium => "Medium",
        DefectSeverity.LowMedium => "Low-medium",
        _ => severity.ToString()
    };

    private static string DescribeGranularity(DashboardBucketGranularity granularity) => granularity switch
    {
        DashboardBucketGranularity.Hour => "Hourly",
        DashboardBucketGranularity.Day => "Daily",
        DashboardBucketGranularity.Week => "Weekly",
        DashboardBucketGranularity.Month => "Monthly",
        _ => throw new ArgumentOutOfRangeException(nameof(granularity))
    };
}
