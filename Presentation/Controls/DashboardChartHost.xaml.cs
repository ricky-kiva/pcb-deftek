using System;
using System.Windows;
using System.Windows.Controls;
using pcb_deftek.Presentation.ViewModels;

namespace pcb_deftek.Presentation.Controls;

/// <summary>
/// Shared chart rendering surface used by metric cards and the enlarged modal.
/// </summary>
public partial class DashboardChartHost : UserControl
{
    public DashboardChartHost()
    {
        InitializeComponent();
        UpdateCard();
    }

    /// <summary>Gets or sets the card projection to render.</summary>
    public static readonly DependencyProperty CardProperty = DependencyProperty.Register(
        nameof(Card),
        typeof(DashboardMetricCardViewModel),
        typeof(DashboardChartHost),
        new PropertyMetadata(null, OnCardChanged));

    /// <summary>Raised when the displayed chart is clicked or keyboard-activated.</summary>
    public event EventHandler? ChartActivated;

    public DashboardMetricCardViewModel? Card
    {
        get => (DashboardMetricCardViewModel?)GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }

    internal int VisibleChartCount =>
        (PassFailChart.Visibility == Visibility.Visible ? 1 : 0) +
        (MetricChart.Visibility == Visibility.Visible ? 1 : 0) +
        (BarChart.Visibility == Visibility.Visible ? 1 : 0);

    private static void OnCardChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DashboardChartHost)d).UpdateCard();

    private void UpdateCard()
    {
        DashboardMetricCardViewModel? card = Card;
        PassFailChart.Visibility = card?.Kind == DashboardMetricCardKind.PassFail
            ? Visibility.Visible
            : Visibility.Collapsed;
        MetricChart.Visibility = card?.Kind == DashboardMetricCardKind.TimeSeries
            ? Visibility.Visible
            : Visibility.Collapsed;
        BarChart.Visibility = card?.Kind == DashboardMetricCardKind.Distribution
            ? Visibility.Visible
            : Visibility.Collapsed;

        PassFailChart.Points = card?.PassFailSeries;
        PassFailChart.AccessibleSummary = card?.AccessibleSummary;

        MetricChart.Points = card?.TimeSeries;
        MetricChart.AccessibleSummary = card?.AccessibleSummary;
        MetricChart.SeriesName = card?.Title ?? "Series";
        MetricChart.GranularityLabel = card?.GranularityLabel ?? string.Empty;
        MetricChart.SeriesBrush = card?.SeriesColorHex is { } colorHex
            ? ChartRenderHelpers.BrushFromHex(colorHex, null)
            : null;

        BarChart.Items = card?.ClassCounts;
        BarChart.AccessibleSummary = card?.Kind == DashboardMetricCardKind.Distribution
            ? null
            : card?.AccessibleSummary;
    }

    private void OnChildChartActivated(object? sender, EventArgs e) =>
        ChartActivated?.Invoke(this, EventArgs.Empty);
}
