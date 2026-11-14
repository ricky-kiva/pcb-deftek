using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Infrastructure.Export;

/// <summary>
/// Kind of chart carried by a report metric block.
/// </summary>
public enum DashboardReportMetricKind
{
    TimeSeries = 0,
    PassFail = 1,
    Distribution = 2
}

/// <summary>One adaptive time-series point rendered by a report chart.</summary>
public sealed record DashboardReportTimeSeriesPoint
{
    /// <summary>Initializes a new instance of the <see cref="DashboardReportTimeSeriesPoint"/> class.</summary>
    /// <param name="label">Axis label for the bucket.</param>
    /// <param name="value">Non-negative bucket value.</param>
    public DashboardReportTimeSeriesPoint(string label, int value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Series values cannot be negative.");
        }

        Label = label.Trim();
        Value = value;
    }

    /// <summary>Gets the axis label for the bucket.</summary>
    public string Label { get; }

    /// <summary>Gets the bucket value.</summary>
    public int Value { get; }
}

/// <summary>One PASS/FAIL point rendered by the inspection-rate chart.</summary>
public sealed record DashboardReportPassFailPoint
{
    /// <summary>Initializes a new instance of the <see cref="DashboardReportPassFailPoint"/> class.</summary>
    /// <param name="label">Axis label for the bucket.</param>
    /// <param name="passes">Non-negative PASS count.</param>
    /// <param name="fails">Non-negative FAIL count.</param>
    public DashboardReportPassFailPoint(string label, int passes, int fails)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (passes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(passes), passes, "PASS counts cannot be negative.");
        }

        if (fails < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fails), fails, "FAIL counts cannot be negative.");
        }

        Label = label.Trim();
        Passes = passes;
        Fails = fails;
    }

    /// <summary>Gets the axis label for the bucket.</summary>
    public string Label { get; }

    /// <summary>Gets the PASS count.</summary>
    public int Passes { get; }

    /// <summary>Gets the FAIL count.</summary>
    public int Fails { get; }
}

/// <summary>Human-readable filter summary printed in the report header.</summary>
public sealed record DashboardReportFilterSummary
{
    /// <summary>Initializes a new instance of the <see cref="DashboardReportFilterSummary"/> class.</summary>
    /// <param name="dateRange">Rendered date-range description.</param>
    /// <param name="defectClasses">Rendered defect-class description.</param>
    /// <param name="severities">Rendered severity description.</param>
    public DashboardReportFilterSummary(string dateRange, string defectClasses, string severities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dateRange);
        ArgumentException.ThrowIfNullOrWhiteSpace(defectClasses);
        ArgumentException.ThrowIfNullOrWhiteSpace(severities);

        DateRange = dateRange.Trim();
        DefectClasses = defectClasses.Trim();
        Severities = severities.Trim();
    }

    /// <summary>Gets the rendered date-range description.</summary>
    public string DateRange { get; }

    /// <summary>Gets the rendered defect-class description.</summary>
    public string DefectClasses { get; }

    /// <summary>Gets the rendered severity description.</summary>
    public string Severities { get; }
}

/// <summary>
/// One metric block in the report. A metric carries exactly one chart payload, selected by its factory.
/// </summary>
public sealed record DashboardReportMetric
{
    private DashboardReportMetric(
        string key,
        string title,
        string displayValue,
        string caption,
        DashboardReportMetricKind kind,
        IReadOnlyList<DashboardReportTimeSeriesPoint> timeSeries,
        IReadOnlyList<DashboardReportPassFailPoint> passFailSeries,
        IReadOnlyList<DashboardClassCount> classCounts,
        string? seriesColorHex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(caption);
        ArgumentNullException.ThrowIfNull(timeSeries);
        ArgumentNullException.ThrowIfNull(passFailSeries);
        ArgumentNullException.ThrowIfNull(classCounts);

        string? normalizedColor = string.IsNullOrWhiteSpace(seriesColorHex)
            ? null
            : seriesColorHex.Trim();
        if (normalizedColor is not null && !IsValidHexColor(normalizedColor))
        {
            throw new ArgumentException("Series color must use the #RRGGBB format.", nameof(seriesColorHex));
        }

        Key = key.Trim();
        Title = title.Trim();
        DisplayValue = displayValue.Trim();
        Caption = caption.Trim();
        Kind = kind;
        TimeSeries = Array.AsReadOnly(timeSeries.ToArray());
        PassFailSeries = Array.AsReadOnly(passFailSeries.ToArray());
        ClassCounts = Array.AsReadOnly(classCounts.ToArray());
        SeriesColorHex = normalizedColor;
    }

    /// <summary>Gets the stable metric key.</summary>
    public string Key { get; }

    /// <summary>Gets the metric title.</summary>
    public string Title { get; }

    /// <summary>Gets the formatted primary value.</summary>
    public string DisplayValue { get; }

    /// <summary>Gets the supporting caption.</summary>
    public string Caption { get; }

    /// <summary>Gets the chart kind.</summary>
    public DashboardReportMetricKind Kind { get; }

    /// <summary>Gets adaptive single-series points; empty unless <see cref="Kind"/> is time-series.</summary>
    public IReadOnlyList<DashboardReportTimeSeriesPoint> TimeSeries { get; }

    /// <summary>Gets dual PASS/FAIL points; empty unless <see cref="Kind"/> is pass/fail.</summary>
    public IReadOnlyList<DashboardReportPassFailPoint> PassFailSeries { get; }

    /// <summary>Gets class distribution counts; empty unless <see cref="Kind"/> is distribution.</summary>
    public IReadOnlyList<DashboardClassCount> ClassCounts { get; }

    /// <summary>Gets the optional single-series accent color in <c>#RRGGBB</c> form.</summary>
    public string? SeriesColorHex { get; }

    /// <summary>Creates a single-series time metric.</summary>
    /// <param name="key">Stable metric key.</param>
    /// <param name="title">Metric title.</param>
    /// <param name="displayValue">Formatted primary value.</param>
    /// <param name="caption">Supporting caption.</param>
    /// <param name="timeSeries">Adaptive points; required.</param>
    /// <param name="seriesColorHex">Optional single-series accent color in <c>#RRGGBB</c> form.</param>
    /// <returns>The metric block.</returns>
    public static DashboardReportMetric CreateTimeSeries(
        string key,
        string title,
        string displayValue,
        string caption,
        IReadOnlyList<DashboardReportTimeSeriesPoint> timeSeries,
        string? seriesColorHex = null)
    {
        ArgumentNullException.ThrowIfNull(timeSeries);

        return new DashboardReportMetric(
            key,
            title,
            displayValue,
            caption,
            DashboardReportMetricKind.TimeSeries,
            timeSeries,
            Array.Empty<DashboardReportPassFailPoint>(),
            Array.Empty<DashboardClassCount>(),
            seriesColorHex);
    }

    /// <summary>Creates a dual PASS/FAIL metric.</summary>
    /// <param name="key">Stable metric key.</param>
    /// <param name="title">Metric title.</param>
    /// <param name="displayValue">Formatted primary value.</param>
    /// <param name="caption">Supporting caption.</param>
    /// <param name="passFailSeries">Adaptive PASS/FAIL points; required.</param>
    /// <returns>The metric block.</returns>
    public static DashboardReportMetric CreatePassFail(
        string key,
        string title,
        string displayValue,
        string caption,
        IReadOnlyList<DashboardReportPassFailPoint> passFailSeries)
    {
        ArgumentNullException.ThrowIfNull(passFailSeries);

        return new DashboardReportMetric(
            key,
            title,
            displayValue,
            caption,
            DashboardReportMetricKind.PassFail,
            Array.Empty<DashboardReportTimeSeriesPoint>(),
            passFailSeries,
            Array.Empty<DashboardClassCount>(),
            null);
    }

    /// <summary>Creates a defect-distribution metric.</summary>
    /// <param name="key">Stable metric key.</param>
    /// <param name="title">Metric title.</param>
    /// <param name="displayValue">Formatted primary value.</param>
    /// <param name="caption">Supporting caption.</param>
    /// <param name="classCounts">Per-class counts; required.</param>
    /// <returns>The metric block.</returns>
    public static DashboardReportMetric CreateDistribution(
        string key,
        string title,
        string displayValue,
        string caption,
        IReadOnlyList<DashboardClassCount> classCounts)
    {
        ArgumentNullException.ThrowIfNull(classCounts);

        return new DashboardReportMetric(
            key,
            title,
            displayValue,
            caption,
            DashboardReportMetricKind.Distribution,
            Array.Empty<DashboardReportTimeSeriesPoint>(),
            Array.Empty<DashboardReportPassFailPoint>(),
            classCounts,
            null);
    }

    private static bool IsValidHexColor(string value)
    {
        if (value.Length != 7 || value[0] != '#')
        {
            return false;
        }

        for (int i = 1; i < value.Length; i++)
        {
            if (!Uri.IsHexDigit(value[i]))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>One defect row in the report evidence table.</summary>
public sealed record DashboardReportDefect
{
    /// <summary>Initializes a new instance of the <see cref="DashboardReportDefect"/> class.</summary>
    /// <param name="className">Defect class display name.</param>
    /// <param name="confidence">Normalized confidence between 0.0 and 1.0.</param>
    /// <param name="severity">Diagnostic severity.</param>
    public DashboardReportDefect(string className, float confidence, DefectSeverity severity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        if (!float.IsFinite(confidence) || confidence < 0.0f || confidence > 1.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidence),
                confidence,
                "Confidence must be normalized between 0.0 and 1.0.");
        }

        ClassName = className.Trim();
        Confidence = confidence;
        Severity = severity;
    }

    /// <summary>Gets the defect class display name.</summary>
    public string ClassName { get; }

    /// <summary>Gets the normalized confidence.</summary>
    public float Confidence { get; }

    /// <summary>Gets the diagnostic severity.</summary>
    public DefectSeverity Severity { get; }
}

/// <summary>One inspection evidence block in the report.</summary>
public sealed record DashboardReportInspection
{
    /// <summary>Initializes a new instance of the <see cref="DashboardReportInspection"/> class.</summary>
    /// <param name="id">Inspection identifier.</param>
    /// <param name="name">Inspection display name.</param>
    /// <param name="createdAtLocal">Local detection timestamp.</param>
    /// <param name="mode">Detection mode.</param>
    /// <param name="status">PASS/FAIL status.</param>
    /// <param name="stagedImagePath">Absolute path to the annotated PNG staged for the report.</param>
    /// <param name="defects">Defect rows; empty only for PASS inspections.</param>
    public DashboardReportInspection(
        Guid id,
        string name,
        DateTime createdAtLocal,
        DetectionMode mode,
        InspectionStatus status,
        string stagedImagePath,
        IReadOnlyList<DashboardReportDefect> defects)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Inspection identifier cannot be empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedImagePath);
        ArgumentNullException.ThrowIfNull(defects);

        DashboardReportDefect[] copiedDefects = defects.ToArray();
        if (status == InspectionStatus.Pass && copiedDefects.Length > 0)
        {
            throw new ArgumentException("PASS inspections cannot carry defects.", nameof(defects));
        }

        if (status == InspectionStatus.Fail && copiedDefects.Length == 0)
        {
            throw new ArgumentException("FAIL inspections must carry at least one defect.", nameof(defects));
        }

        Id = id;
        Name = name.Trim();
        CreatedAtLocal = createdAtLocal;
        Mode = mode;
        Status = status;
        StagedImagePath = Path.GetFullPath(stagedImagePath.Trim());
        Defects = Array.AsReadOnly(copiedDefects);
    }

    /// <summary>Gets the inspection identifier.</summary>
    public Guid Id { get; }

    /// <summary>Gets the inspection display name.</summary>
    public string Name { get; }

    /// <summary>Gets the local detection timestamp.</summary>
    public DateTime CreatedAtLocal { get; }

    /// <summary>Gets the detection mode.</summary>
    public DetectionMode Mode { get; }

    /// <summary>Gets the PASS/FAIL status.</summary>
    public InspectionStatus Status { get; }

    /// <summary>Gets the absolute path to the staged annotated PNG.</summary>
    public string StagedImagePath { get; }

    /// <summary>Gets the defect rows.</summary>
    public IReadOnlyList<DashboardReportDefect> Defects { get; }

    /// <summary>Gets the number of defect rows.</summary>
    public int DefectCount => Defects.Count;

    /// <summary>Gets the display label for the detection mode.</summary>
    public string ModeLabel => Mode switch
    {
        DetectionMode.SingleFile => "Single file",
        DetectionMode.BatchFolder => "Batch folder",
        DetectionMode.LiveCamera => "Live camera",
        _ => Mode.ToString()
    };

    /// <summary>Gets the PASS/FAIL display label.</summary>
    public string StatusLabel => Status == InspectionStatus.Pass ? "PASS" : "FAIL";
}

/// <summary>
/// Immutable content model for one audit report. Contains no WPF or QuestPDF types.
/// </summary>
public sealed record DashboardReportDocument
{
    private const int RequiredMetricCount = 4;

    /// <summary>Initializes a new instance of the <see cref="DashboardReportDocument"/> class.</summary>
    /// <param name="title">Report title.</param>
    /// <param name="subtitle">Report subtitle.</param>
    /// <param name="generatedAtLocal">Local generation timestamp.</param>
    /// <param name="filterSummary">Rendered filter summary.</param>
    /// <param name="metrics">The four metric blocks.</param>
    /// <param name="inspections">Evidence blocks; empty when every matched inspection was skipped.</param>
    public DashboardReportDocument(
        string title,
        string subtitle,
        DateTime generatedAtLocal,
        DashboardReportFilterSummary filterSummary,
        IReadOnlyList<DashboardReportMetric> metrics,
        IReadOnlyList<DashboardReportInspection> inspections)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(subtitle);
        ArgumentNullException.ThrowIfNull(filterSummary);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(inspections);

        DashboardReportMetric[] copiedMetrics = metrics.ToArray();
        if (copiedMetrics.Length != RequiredMetricCount)
        {
            throw new ArgumentException(
                $"A report requires exactly {RequiredMetricCount} metric blocks.",
                nameof(metrics));
        }

        if (copiedMetrics.Select(metric => metric.Key).Distinct(StringComparer.Ordinal).Count() != RequiredMetricCount)
        {
            throw new ArgumentException("Metric keys must be unique.", nameof(metrics));
        }

        DashboardReportInspection[] copiedInspections = inspections.ToArray();
        if (copiedInspections.Select(inspection => inspection.Id).Distinct().Count() != copiedInspections.Length)
        {
            throw new ArgumentException("Inspection identifiers must be unique.", nameof(inspections));
        }

        Title = title.Trim();
        Subtitle = subtitle.Trim();
        GeneratedAtLocal = generatedAtLocal;
        FilterSummary = filterSummary;
        Metrics = Array.AsReadOnly(copiedMetrics);
        Inspections = Array.AsReadOnly(copiedInspections);
    }

    /// <summary>Gets the report title.</summary>
    public string Title { get; }

    /// <summary>Gets the report subtitle.</summary>
    public string Subtitle { get; }

    /// <summary>Gets the local generation timestamp.</summary>
    public DateTime GeneratedAtLocal { get; }

    /// <summary>Gets the rendered filter summary.</summary>
    public DashboardReportFilterSummary FilterSummary { get; }

    /// <summary>Gets the four metric blocks in canonical order.</summary>
    public IReadOnlyList<DashboardReportMetric> Metrics { get; }

    /// <summary>Gets the evidence blocks in report order.</summary>
    public IReadOnlyList<DashboardReportInspection> Inspections { get; }
}
