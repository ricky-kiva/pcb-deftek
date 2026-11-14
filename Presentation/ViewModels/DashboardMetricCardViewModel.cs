using System;
using System.Collections.Generic;
using pcb_deftek.Domain.Contracts;

namespace pcb_deftek.Presentation.ViewModels;

/// <summary>
/// Chart projection carried by a dashboard metric card.
/// </summary>
public enum DashboardMetricCardKind
{
    TimeSeries = 0,
    PassFail = 1,
    Distribution = 2
}

/// <summary>One adaptive time-series point.</summary>
public sealed record DashboardTimeSeriesPoint(string Label, int Value);

/// <summary>One PASS/FAIL bucket point.</summary>
public sealed record DashboardPassFailPoint(string Label, int Passes, int Fails);

/// <summary>
/// Immutable view state for one of the four dashboard metric cards.
/// </summary>
public sealed class DashboardMetricCardViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardMetricCardViewModel"/> class.
    /// </summary>
    public DashboardMetricCardViewModel(
        string key,
        string title,
        string displayValue,
        string caption,
        DashboardMetricCardKind kind,
        IReadOnlyList<DashboardTimeSeriesPoint>? timeSeries = null,
        IReadOnlyList<DashboardPassFailPoint>? passFailSeries = null,
        IReadOnlyList<DashboardClassCount>? classCounts = null,
        string? seriesColorHex = null,
        string? accessibleSummary = null,
        string? granularityLabel = null)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Title = title ?? throw new ArgumentNullException(nameof(title));
        DisplayValue = displayValue ?? throw new ArgumentNullException(nameof(displayValue));
        Caption = caption ?? throw new ArgumentNullException(nameof(caption));
        Kind = kind;
        TimeSeries = timeSeries ?? Array.Empty<DashboardTimeSeriesPoint>();
        PassFailSeries = passFailSeries ?? Array.Empty<DashboardPassFailPoint>();
        ClassCounts = classCounts ?? Array.Empty<DashboardClassCount>();
        SeriesColorHex = seriesColorHex;
        AccessibleSummary = accessibleSummary ?? $"{title}: {displayValue}. {caption}";
        GranularityLabel = granularityLabel ?? string.Empty;
    }

    /// <summary>Gets the stable card key used for persistence.</summary>
    public string Key { get; }

    /// <summary>Gets the card title.</summary>
    public string Title { get; }

    /// <summary>Gets the formatted primary value.</summary>
    public string DisplayValue { get; }

    /// <summary>Gets the supporting caption.</summary>
    public string Caption { get; }

    /// <summary>Gets the chart kind.</summary>
    public DashboardMetricCardKind Kind { get; }

    /// <summary>Gets adaptive single-series points for PCBs/defects.</summary>
    public IReadOnlyList<DashboardTimeSeriesPoint> TimeSeries { get; }

    /// <summary>Gets dual PASS/FAIL points.</summary>
    public IReadOnlyList<DashboardPassFailPoint> PassFailSeries { get; }

    /// <summary>Gets the class distribution.</summary>
    public IReadOnlyList<DashboardClassCount> ClassCounts { get; }

    /// <summary>Gets the optional series accent color.</summary>
    public string? SeriesColorHex { get; }

    /// <summary>Gets the accessible summary for the card.</summary>
    public string AccessibleSummary { get; }

    /// <summary>Gets the adaptive bucket granularity label, if applicable.</summary>
    public string GranularityLabel { get; }
}
