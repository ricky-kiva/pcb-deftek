using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Presentation.Controls;

/// <summary>
/// Vertical defect-class distribution chart with nine bars in taxonomy order,
/// class colors, hover values, and keyboard activation.
/// </summary>
public partial class DefectBarChart : UserControl
{
    private readonly List<HoverTarget> _hoverTargets = new();

    public DefectBarChart()
    {
        InitializeComponent();
        UpdateAutomationName();
    }

    /// <summary>Gets or sets the nine class counts.</summary>
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items),
        typeof(IEnumerable<DashboardClassCount>),
        typeof(DefectBarChart),
        new PropertyMetadata(null, OnDataChanged));

    /// <summary>Gets or sets an explicit accessible summary.</summary>
    public static readonly DependencyProperty AccessibleSummaryProperty = DependencyProperty.Register(
        nameof(AccessibleSummary),
        typeof(string),
        typeof(DefectBarChart),
        new PropertyMetadata(null, OnDataChanged));

    /// <summary>Raised when the chart is clicked or activated from the keyboard.</summary>
    public event EventHandler? ChartActivated;

    public IEnumerable<DashboardClassCount>? Items
    {
        get => (IEnumerable<DashboardClassCount>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public string? AccessibleSummary
    {
        get => (string?)GetValue(AccessibleSummaryProperty);
        set => SetValue(AccessibleSummaryProperty, value);
    }

    internal int RenderedBarCount { get; private set; }

    internal int RenderedLabelCount { get; private set; }

    internal IReadOnlyList<string> RenderedBarColorHexes { get; private set; } = Array.Empty<string>();

    internal bool IsEmptyStateVisible => EmptyStateText.Visibility == Visibility.Visible;

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
        var chart = (DefectBarChart)d;
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
        RenderedBarCount = 0;
        RenderedLabelCount = 0;
        RenderedBarColorHexes = Array.Empty<string>();

        var items = Items?
            .OrderBy(item => DefectKnowledgeBase.GetInfo(item.Class).ClassId)
            .ToList() ?? new List<DashboardClassCount>();

        if (items.Count == 0 || ActualWidth < 24 || ActualHeight < 24)
        {
            EmptyStateText.Visibility = Visibility.Visible;
            UpdateAutomationName();
            return;
        }

        EmptyStateText.Visibility = Visibility.Collapsed;
        PlotArea = ChartRenderHelpers.CreatePlotRect(ActualWidth, ActualHeight);
        Brush gridBrush = ChartRenderHelpers.GetBrush(this, "ControlStrokeColorDefaultBrush", Brushes.Gray);
        Brush labelBrush = ChartRenderHelpers.GetBrush(this, "TextFillColorSecondaryBrush", Brushes.Gray);

        ChartRenderHelpers.AddLine(
            PlotCanvas, PlotArea.Left, PlotArea.Bottom, PlotArea.Right, PlotArea.Bottom, gridBrush, 1, 0.8);

        int maxValue = Math.Max(1, items.Max(item => item.Count));
        double slotWidth = PlotArea.Width / items.Count;
        double barWidth = Math.Min(30, Math.Max(7, slotWidth * 0.62));
        double labelSize = ActualWidth < 340 ? 9 : 10;

        var colors = new List<string>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            DashboardClassCount item = items[i];
            DefectClassInfo info = DefectKnowledgeBase.GetInfo(item.Class);
            double centerX = PlotArea.Left + (slotWidth * (i + 0.5));
            double height = (Math.Clamp(item.Count, 0, maxValue) / (double)maxValue) * PlotArea.Height;
            if (item.Count <= 0)
            {
                height = 2;
            }

            Brush fill = ChartRenderHelpers.BrushFromHex(info.ColorHex, Brushes.Gray) ?? Brushes.Gray;
            colors.Add(info.ColorHex);
            var bar = new Rectangle
            {
                Width = barWidth,
                Height = Math.Max(2, height),
                Fill = fill,
                RadiusX = 2,
                RadiusY = 2,
                IsHitTestVisible = false,
                ToolTip = $"{info.DisplayName}: {item.Count}"
            };
            Canvas.SetLeft(bar, centerX - (barWidth / 2));
            Canvas.SetTop(bar, PlotArea.Bottom - bar.Height);
            PlotCanvas.Children.Add(bar);

            ChartRenderHelpers.AddText(
                LabelCanvas, item.Abbreviation, centerX, PlotArea.Bottom + 5, labelBrush, labelSize);
            _hoverTargets.Add(new HoverTarget(
                new Rect(centerX - (barWidth / 2), PlotArea.Top, barWidth, PlotArea.Height),
                $"{info.DisplayName}: {item.Count}"));
            RenderedLabelCount++;
        }

        RenderedBarCount = items.Count;
        RenderedBarColorHexes = colors;
        UpdateAutomationName();
    }

    private void OnChartMouseMove(object sender, MouseEventArgs e)
    {
        if (_hoverTargets.Count == 0)
        {
            HideTooltip();
            return;
        }

        Point position = e.GetPosition(Surface);
        HoverTarget? target = _hoverTargets.FirstOrDefault(candidate => candidate.Bounds.Contains(position));
        if (target is not null)
        {
            ShowTooltip(target);
        }
        else
        {
            HideTooltip();
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
        double anchorX = target.Bounds.Left + (target.Bounds.Width / 2);
        double left = Math.Clamp(anchorX + 10, 0, Math.Max(0, ActualWidth - TooltipBorder.DesiredSize.Width));
        double top = Math.Clamp(PlotArea.Top + 4, 0, Math.Max(0, ActualHeight - TooltipBorder.DesiredSize.Height));
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
        var items = Items?
            .OrderBy(item => DefectKnowledgeBase.GetInfo(item.Class).ClassId)
            .ToList() ?? new List<DashboardClassCount>();
        if (items.Count == 0)
        {
            return "Defect distribution chart. No data for this range.";
        }

        return "Defect distribution: " + string.Join(
            ", ",
            items.Select(item => $"{DefectKnowledgeBase.GetInfo(item.Class).DisplayName} {item.Count}"));
    }

    private sealed record HoverTarget(Rect Bounds, string Text);
}
