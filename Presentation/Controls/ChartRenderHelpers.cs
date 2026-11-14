using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace pcb_deftek.Presentation.Controls;

/// <summary>
/// Shared geometry, brush, and label helpers for the lightweight dashboard charts.
/// </summary>
internal static class ChartRenderHelpers
{
    internal static int CalculateLabelStep(int pointCount, double availableWidth, double minimumSpacing = 62)
    {
        if (pointCount <= 1 || availableWidth <= 0)
        {
            return 1;
        }

        int maxLabels = Math.Max(2, (int)Math.Floor(availableWidth / Math.Max(1, minimumSpacing)));
        return Math.Max(1, (int)Math.Ceiling((double)pointCount / maxLabels));
    }

    internal static Rect CreatePlotRect(double width, double height, bool hasXLabels = true)
    {
        double left = width < 260 ? 30 : 38;
        double right = 12;
        double top = 12;
        double bottom = hasXLabels ? 26 : 12;
        double plotWidth = Math.Max(1, width - left - right);
        double plotHeight = Math.Max(1, height - top - bottom);
        return new Rect(left, top, plotWidth, plotHeight);
    }

    internal static Brush GetBrush(FrameworkElement owner, string resourceKey, Brush fallback)
    {
        return owner.TryFindResource(resourceKey) as Brush ?? fallback;
    }

    internal static Brush? BrushFromHex(string? colorHex, Brush? fallback)
    {
        if (colorHex is { Length: 7 } && colorHex[0] == '#' &&
            uint.TryParse(colorHex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            var brush = new SolidColorBrush(Color.FromRgb(
                (byte)(rgb >> 16),
                (byte)(rgb >> 8),
                (byte)rgb));
            brush.Freeze();
            return brush;
        }

        return fallback;
    }

    internal static void AddText(
        Canvas canvas,
        string text,
        double centerX,
        double top,
        Brush foreground,
        double fontSize,
        bool bold = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = foreground,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false
        };
        block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double left = centerX - (block.DesiredSize.Width / 2);
        double maxLeft = canvas.ActualWidth - block.DesiredSize.Width;
        Canvas.SetLeft(block, maxLeft > 0 ? Math.Clamp(left, 0, maxLeft) : left);
        Canvas.SetTop(block, top);
        canvas.Children.Add(block);
    }

    internal static void AddLine(
        Canvas canvas,
        double x1,
        double y1,
        double x2,
        double y2,
        Brush stroke,
        double thickness = 1,
        double opacity = 1)
    {
        canvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = stroke,
            StrokeThickness = thickness,
            Opacity = opacity,
            IsHitTestVisible = false
        });
    }

    internal static void AddEllipse(
        Canvas canvas,
        double centerX,
        double centerY,
        double radius,
        Brush fill,
        Brush? stroke = null,
        double strokeThickness = 0)
    {
        canvas.Children.Add(new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = strokeThickness,
            IsHitTestVisible = false,
            RenderTransform = new TranslateTransform(centerX - radius, centerY - radius)
        });
    }
}
