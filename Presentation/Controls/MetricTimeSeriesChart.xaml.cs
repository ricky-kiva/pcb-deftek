using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using pcb_deftek.Presentation.ViewModels;

namespace pcb_deftek.Presentation.Controls;

/// <summary>
/// Lightweight single-series adaptive time chart for PCB scan and defect volume.
/// </summary>
public partial class MetricTimeSeriesChart : UserControl
{
    private readonly List<HoverTarget> _hoverTargets = new();
    private int _labelStep = 1;

    public MetricTimeSeriesChart()
    {
        InitializeComponent();
        UpdateAutomationName();
    }

    /// <summary>Gets or sets the adaptive time-series points.</summary>
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points),
        typeof(IEnumerable<DashboardTimeSeriesPoint>),
        typeof(MetricTimeSeriesChart),
        new PropertyMetadata(null, OnDataChanged));

    /// <summary>Gets or sets the series brush.</summary>
    public static readonly DependencyProperty SeriesBrushProperty = DependencyProperty.Register(
        nameof(SeriesBrush),
        typeof(Brush),
        typeof(MetricTimeSeriesChart),
        new PropertyMetadata(null, OnVisualChanged));

    /// <summary>Gets or sets the accessible series name.</summary>
    public static readonly DependencyProperty SeriesNameProperty = DependencyProperty.Register(
        nameof(SeriesName),
        typeof(string),
        typeof(MetricTimeSeriesChart),
        new PropertyMetadata("Series", OnVisualChanged));

    /// <summary>Gets or sets the bucket granularity label.</summary>
    public static readonly DependencyProperty GranularityLabelProperty = DependencyProperty.Register(
        nameof(GranularityLabel),
        typeof(string),
        typeof(MetricTimeSeriesChart),
        new PropertyMetadata(string.Empty, OnVisualChanged));

    /// <summary>Gets or sets an explicit accessible summary.</summary>
    public static readonly DependencyProperty AccessibleSummaryProperty = DependencyProperty.Register(
        nameof(AccessibleSummary),
        typeof(string),
        typeof(MetricTimeSeriesChart),
        new PropertyMetadata(null, OnVisualChanged));

    /// <summary>Raised when the chart is clicked or activated from the keyboard.</summary>
    public event EventHandler? ChartActivated;

    public IEnumerable<DashboardTimeSeriesPoint>? Points
    {
        get => (IEnumerable<DashboardTimeSeriesPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public Brush? SeriesBrush
    {
        get => (Brush?)GetValue(SeriesBrushProperty);
        set => SetValue(SeriesBrushProperty, value);
    }

    public string SeriesName
    {
        get => (string)GetValue(SeriesNameProperty);
        set => SetValue(SeriesNameProperty, value);
    }

    public string GranularityLabel
    {
        get => (string)GetValue(GranularityLabelProperty);
        set => SetValue(GranularityLabelProperty, value);
    }

    public string? AccessibleSummary
    {
        get => (string?)GetValue(AccessibleSummaryProperty);
        set => SetValue(AccessibleSummaryProperty, value);
    }

    internal int RenderedPointCount { get; private set; }

    internal int RenderedLabelCount { get; private set; }

    internal int RenderedSeriesCount { get; private set; }

    internal bool IsEmptyStateVisible => EmptyStateText.Visibility == Visibility.Visible;

    internal int LabelStep => _labelStep;

    internal string AccessibleSummaryText => AutomationProperties.GetName(this);

    internal string TooltipTextValue => TooltipText.Text;

    internal Rect PlotArea { get; private set; }

    internal void PrepareForTest() => RenderChart();

    internal void ShowHoverForTest(int index)
    {
        if (index >= 0 && index < _hoverTargets.Count)
        {
            ShowTooltip(_hoverTargets[index]);
        }
    }

    internal void ActivateForTest() => ActivateChart();

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (MetricTimeSeriesChart)d;
        chart.UpdateAutomationName();
        if (chart.IsLoaded)
        {
            chart.RenderChart();
        }
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (MetricTimeSeriesChart)d;
        chart.UpdateAutomationName();
        if (chart.IsLoaded)
        {
            chart.RenderChart();
        }
    }

    private void OnChartLoaded(object sender, RoutedEventArgs e) => RenderChart();

    private void OnChartSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            RenderChart();
        }
    }

    private void RenderChart()
    {
        PlotCanvas.Children.Clear();
        LabelCanvas.Children.Clear();
        _hoverTargets.Clear();
        HideTooltip();
        RenderedPointCount = 0;
        RenderedLabelCount = 0;
        RenderedSeriesCount = 0;

        var points = Points?.ToList() ?? new List<DashboardTimeSeriesPoint>();
        if (points.Count == 0 || ActualWidth < 24 || ActualHeight < 24)
        {
            EmptyStateText.Visibility = Visibility.Visible;
            UpdateAutomationName();
            return;
        }

        EmptyStateText.Visibility = Visibility.Collapsed;
        PlotArea = ChartRenderHelpers.CreatePlotRect(ActualWidth, ActualHeight);
        Brush gridBrush = ChartRenderHelpers.GetBrush(this, "ControlStrokeColorDefaultBrush", Brushes.Gray);
        Brush labelBrush = ChartRenderHelpers.GetBrush(this, "TextFillColorSecondaryBrush", Brushes.Gray);
        Brush seriesBrush = SeriesBrush ??
            ChartRenderHelpers.GetBrush(this, "AccentFillColorDefaultBrush", Brushes.SteelBlue);

        int maxValue = Math.Max(1, points.Max(point => point.Value));
        DrawAxes(points, maxValue, gridBrush, labelBrush);

        if (points.Count == 1)
        {
            double x = PlotArea.Left + (PlotArea.Width / 2);
            double y = ValueToY(points[0].Value, maxValue);
            ChartRenderHelpers.AddEllipse(PlotCanvas, x, y, 3.4, seriesBrush);
            RenderedSeriesCount = 1;
            _hoverTargets.Add(new HoverTarget(new Point(x, y), $"{points[0].Label}: {points[0].Value}"));
        }
        else
        {
            var line = new Polyline
            {
                Stroke = seriesBrush,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false
            };

            for (int i = 0; i < points.Count; i++)
            {
                double x = PlotArea.Left + ((PlotArea.Width * i) / (points.Count - 1));
                double y = ValueToY(points[i].Value, maxValue);
                line.Points.Add(new Point(x, y));
                ChartRenderHelpers.AddEllipse(PlotCanvas, x, y, 2.4, seriesBrush);
                _hoverTargets.Add(new HoverTarget(new Point(x, y), $"{points[i].Label}: {points[i].Value}"));
            }

            PlotCanvas.Children.Add(line);
            RenderedSeriesCount = 1;
        }

        RenderedPointCount = points.Count;
        UpdateAutomationName();
    }

    private void DrawAxes(
        IReadOnlyList<DashboardTimeSeriesPoint> points,
        int maxValue,
        Brush gridBrush,
        Brush labelBrush)
    {
        _labelStep = ChartRenderHelpers.CalculateLabelStep(points.Count, PlotArea.Width);
        ChartRenderHelpers.AddLine(
            PlotCanvas, PlotArea.Left, PlotArea.Bottom, PlotArea.Right, PlotArea.Bottom, gridBrush, 1, 0.7);
        ChartRenderHelpers.AddLine(
            PlotCanvas, PlotArea.Left, PlotArea.Top, PlotArea.Left, PlotArea.Bottom, gridBrush, 1, 0.7);

        for (int step = 0; step <= 2; step++)
        {
            if (step == 1 && maxValue < 2)
            {
                continue;
            }

            double fraction = step / 2.0;
            double y = PlotArea.Bottom - (PlotArea.Height * fraction);
            ChartRenderHelpers.AddLine(
                PlotCanvas, PlotArea.Left, y, PlotArea.Right, y, gridBrush, 1, 0.2);
            ChartRenderHelpers.AddText(
                LabelCanvas,
                ((int)Math.Round(maxValue * fraction)).ToString(),
                PlotArea.Left - 12,
                y - 8,
                labelBrush,
                9);
        }

        for (int i = 0; i < points.Count; i++)
        {
            if (i % _labelStep != 0 && i != points.Count - 1)
            {
                continue;
            }

            double x = points.Count == 1
                ? PlotArea.Left + (PlotArea.Width / 2)
                : PlotArea.Left + ((PlotArea.Width * i) / (points.Count - 1));
            ChartRenderHelpers.AddText(LabelCanvas, points[i].Label, x, PlotArea.Bottom + 5, labelBrush, 9);
            RenderedLabelCount++;
        }
    }

    private double ValueToY(int value, int maxValue) =>
        PlotArea.Bottom - ((Math.Clamp(value, 0, maxValue) / (double)maxValue) * PlotArea.Height);

    private void OnChartMouseMove(object sender, MouseEventArgs e)
    {
        if (_hoverTargets.Count == 0)
        {
            HideTooltip();
            return;
        }

        Point position = e.GetPosition(Surface);
        HoverTarget? nearest = null;
        double nearestDistance = double.MaxValue;
        foreach (var target in _hoverTargets)
        {
            double dx = target.Position.X - position.X;
            double dy = target.Position.Y - position.Y;
            double distance = (dx * dx) + (dy * dy);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = target;
            }
        }

        if (nearest is not null)
        {
            ShowTooltip(nearest);
        }
    }

    private void OnChartMouseLeave(object sender, MouseEventArgs e) => HideTooltip();

    private void OnChartMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => ActivateChart();

    private void OnChartKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space)
        {
            ActivateChart();
            e.Handled = true;
        }
    }

    private void ActivateChart()
    {
        Focus();
        ChartActivated?.Invoke(this, EventArgs.Empty);
    }

    private void ShowTooltip(HoverTarget target)
    {
        TooltipText.Text = target.Text;
        TooltipBorder.Visibility = Visibility.Visible;
        TooltipBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double left = Math.Clamp(target.Position.X + 10, 0, Math.Max(0, ActualWidth - TooltipBorder.DesiredSize.Width));
        double top = Math.Clamp(target.Position.Y - TooltipBorder.DesiredSize.Height - 8, 0, Math.Max(0, ActualHeight - TooltipBorder.DesiredSize.Height));
        Canvas.SetLeft(TooltipBorder, left);
        Canvas.SetTop(TooltipBorder, top);
    }

    private void HideTooltip()
    {
        TooltipBorder.Visibility = Visibility.Collapsed;
        TooltipText.Text = string.Empty;
    }

    private void UpdateAutomationName()
    {
        AutomationProperties.SetName(this, AccessibleSummary ?? BuildSummary());
    }

    private string BuildSummary()
    {
        var points = Points?.ToList() ?? new List<DashboardTimeSeriesPoint>();
        if (points.Count == 0)
        {
            return $"{SeriesName} chart. No data for this range.";
        }

        string granularity = string.IsNullOrWhiteSpace(GranularityLabel) ? "adaptive" : GranularityLabel;
        return $"{SeriesName} chart across {points.Count} {granularity} buckets. Total {points.Sum(point => point.Value)}.";
    }

    private sealed record HoverTarget(Point Position, string Text);
}
