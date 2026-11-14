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
/// Lightweight dual-series PASS/FAIL chart with hover values, label thinning,
/// keyboard activation, and an accessible summary.
/// </summary>
public partial class PassFailLineChart : UserControl
{
    private readonly List<HoverTarget> _hoverTargets = new();
    private int _labelStep = 1;

    public PassFailLineChart()
    {
        InitializeComponent();
        UpdateAutomationName();
    }

    /// <summary>Gets or sets the adaptive PASS/FAIL points.</summary>
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points),
        typeof(IEnumerable<DashboardPassFailPoint>),
        typeof(PassFailLineChart),
        new PropertyMetadata(null, OnDataChanged));

    /// <summary>Gets or sets an explicit accessible summary.</summary>
    public static readonly DependencyProperty AccessibleSummaryProperty = DependencyProperty.Register(
        nameof(AccessibleSummary),
        typeof(string),
        typeof(PassFailLineChart),
        new PropertyMetadata(null, OnAccessibleSummaryChanged));

    /// <summary>Raised when the chart is clicked or activated from the keyboard.</summary>
    public event EventHandler? ChartActivated;

    public IEnumerable<DashboardPassFailPoint>? Points
    {
        get => (IEnumerable<DashboardPassFailPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
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

    internal void PrepareForTest()
    {
        RenderChart();
    }

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
        var chart = (PassFailLineChart)d;
        chart.UpdateAutomationName();
        if (chart.IsLoaded)
        {
            chart.RenderChart();
        }
    }

    private static void OnAccessibleSummaryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((PassFailLineChart)d).UpdateAutomationName();
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

        var points = Points?.ToList() ?? new List<DashboardPassFailPoint>();
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
        Brush passBrush = ChartRenderHelpers.GetBrush(this, "StatusPassBrush", Brushes.SeaGreen);
        Brush failBrush = ChartRenderHelpers.GetBrush(this, "StatusFailBrush", Brushes.IndianRed);

        int maxValue = Math.Max(1, points.Max(point => Math.Max(point.Passes, point.Fails)));
        DrawAxes(points.Count, maxValue, gridBrush, labelBrush);

        DrawSeries(points.Select(point => point.Passes).ToArray(), passBrush);
        DrawSeries(points.Select(point => point.Fails).ToArray(), failBrush);
        RenderedSeriesCount = 2;

        for (int i = 0; i < points.Count; i++)
        {
            double x = points.Count == 1
                ? PlotArea.Left + (PlotArea.Width / 2)
                : PlotArea.Left + ((PlotArea.Width * i) / (points.Count - 1));
            double passY = ValueToY(points[i].Passes, maxValue);
            double failY = ValueToY(points[i].Fails, maxValue);
            ChartRenderHelpers.AddEllipse(PlotCanvas, x, passY, 2.4, passBrush);
            ChartRenderHelpers.AddEllipse(PlotCanvas, x, failY, 2.4, failBrush);
            _hoverTargets.Add(new HoverTarget(
                new Point(x, Math.Min(passY, failY)),
                $"{points[i].Label}: {points[i].Passes} PASS · {points[i].Fails} FAIL"));
        }

        RenderedPointCount = points.Count * 2;
        UpdateAutomationName();
    }

    private void DrawAxes(int pointCount, int maxValue, Brush gridBrush, Brush labelBrush)
    {
        _labelStep = ChartRenderHelpers.CalculateLabelStep(pointCount, PlotArea.Width);
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

        if (Points is null)
        {
            return;
        }

        var points = Points.ToList();
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

    private void DrawSeries(IReadOnlyList<int> values, Brush brush)
    {
        if (values.Count == 0)
        {
            return;
        }

        int maxValue = Math.Max(1, Points!.Max(point => Math.Max(point.Passes, point.Fails)));
        if (values.Count == 1)
        {
            double x = PlotArea.Left + (PlotArea.Width / 2);
            ChartRenderHelpers.AddEllipse(PlotCanvas, x, ValueToY(values[0], maxValue), 3.2, brush);
            return;
        }

        var line = new Polyline
        {
            Stroke = brush,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        };

        for (int i = 0; i < values.Count; i++)
        {
            double x = PlotArea.Left + ((PlotArea.Width * i) / (values.Count - 1));
            line.Points.Add(new Point(x, ValueToY(values[i], maxValue)));
        }

        PlotCanvas.Children.Add(line);
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
        string summary = AccessibleSummary ?? BuildSummary();
        AutomationProperties.SetName(this, summary);
    }

    private string BuildSummary()
    {
        var points = Points?.ToList() ?? new List<DashboardPassFailPoint>();
        if (points.Count == 0)
        {
            return "PASS/FAIL chart. No data for this range.";
        }

        return $"PASS/FAIL chart across {points.Count} buckets. " +
               $"{points.Sum(point => point.Passes)} PASS and {points.Sum(point => point.Fails)} FAIL.";
    }

    private sealed record HoverTarget(Point Position, string Text);
}
