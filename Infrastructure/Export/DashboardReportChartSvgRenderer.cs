using System;
using System.Globalization;
using System.Linq;
using System.Security;
using System.Text;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Infrastructure.Export;

/// <summary>
/// Renders the report's three chart kinds as self-contained SVG strings. The output uses only
/// basic SVG elements, no scripts, and no external resources, so QuestPDF can embed it directly.
/// </summary>
public static class DashboardReportChartSvgRenderer
{
    /// <summary>PASS series color used by the inspection-rate chart.</summary>
    public const string PassColor = "#107C41";

    /// <summary>FAIL series color used by the inspection-rate chart.</summary>
    public const string FailColor = "#C42B1C";

    private const double MinimumLabelSpacing = 70;
    private const string AxisColor = "#8A8A8A";
    private const string GridColor = "#E3E3E3";
    private const string TextColor = "#5F5F5F";
    private const double FontSize = 9;

    /// <summary>
    /// Renders the metric's chart at the requested size.
    /// </summary>
    /// <param name="metric">Metric block carrying one chart payload.</param>
    /// <param name="width">Target width in SVG units.</param>
    /// <param name="height">Target height in SVG units.</param>
    /// <returns>A complete SVG document.</returns>
    public static string Render(DashboardReportMetric metric, double width = 900, double height = 220)
    {
        ArgumentNullException.ThrowIfNull(metric);
        ValidateDimensions(width, height);

        return metric.Kind switch
        {
            DashboardReportMetricKind.TimeSeries => RenderTimeSeries(
                metric.TimeSeries,
                metric.SeriesColorHex ?? "#0066FF",
                width,
                height),
            DashboardReportMetricKind.PassFail => RenderPassFail(metric.PassFailSeries, width, height),
            DashboardReportMetricKind.Distribution => RenderDistribution(metric.ClassCounts, width, height),
            _ => throw new ArgumentOutOfRangeException(nameof(metric), metric.Kind, "Unknown report chart kind.")
        };
    }

    /// <summary>
    /// Calculates a deterministic label step so x-axis labels never overlap.
    /// </summary>
    /// <param name="pointCount">Number of points/buckets.</param>
    /// <param name="availableWidth">Available plot width in SVG units.</param>
    /// <param name="minimumSpacing">Minimum distance between labels.</param>
    /// <returns>A positive step; 1 when no thinning is needed.</returns>
    internal static int CalculateLabelStep(
        int pointCount,
        double availableWidth,
        double minimumSpacing = MinimumLabelSpacing)
    {
        if (pointCount <= 1 || !double.IsFinite(availableWidth) || availableWidth <= 0)
        {
            return 1;
        }

        int maxLabels = Math.Max(2, (int)Math.Floor(availableWidth / Math.Max(1, minimumSpacing)));
        return Math.Max(1, (int)Math.Ceiling((double)pointCount / maxLabels));
    }

    private static string RenderTimeSeries(
        IReadOnlyList<DashboardReportTimeSeriesPoint> points,
        string seriesColor,
        double width,
        double height)
    {
        var svg = Begin(width, height);
        Plot plot = CreatePlot(width, height);
        AppendAxes(svg, plot);

        if (points.Count == 0)
        {
            AppendCenteredText(svg, "No data", plot, height);
            return End(svg);
        }

        int maxValue = Math.Max(1, points.Max(point => point.Value));
        AppendSeries(
            svg,
            points.Select(point => point.Value).ToArray(),
            seriesColor,
            maxValue,
            plot);

        AppendXLabels(
            svg,
            points.Select(point => point.Label).ToArray(),
            points.Count,
            plot);
        return End(svg);
    }

    private static string RenderPassFail(
        IReadOnlyList<DashboardReportPassFailPoint> points,
        double width,
        double height)
    {
        var svg = Begin(width, height);
        Plot plot = CreatePlot(width, height);
        AppendAxes(svg, plot);

        if (points.Count == 0)
        {
            AppendCenteredText(svg, "No data", plot, height);
            return End(svg);
        }

        int maxValue = Math.Max(
            1,
            points.Select(point => Math.Max(point.Passes, point.Fails)).DefaultIfEmpty(0).Max());
        AppendSeries(svg, points.Select(point => point.Passes).ToArray(), PassColor, maxValue, plot);
        AppendSeries(svg, points.Select(point => point.Fails).ToArray(), FailColor, maxValue, plot);
        AppendLegend(svg, plot);

        AppendXLabels(
            svg,
            points.Select(point => point.Label).ToArray(),
            points.Count,
            plot);
        return End(svg);
    }

    private static string RenderDistribution(
        IReadOnlyList<DashboardClassCount> classCounts,
        double width,
        double height)
    {
        var svg = Begin(width, height);
        Plot plot = CreatePlot(width, height);
        AppendAxes(svg, plot);

        if (classCounts.Count == 0)
        {
            AppendCenteredText(svg, "No data", plot, height);
            return End(svg);
        }

        int maxValue = Math.Max(1, classCounts.Max(item => item.Count));
        double slotWidth = plot.Width / classCounts.Count;
        double barWidth = Math.Max(2, slotWidth * 0.62);

        for (int i = 0; i < classCounts.Count; i++)
        {
            DashboardClassCount item = classCounts[i];
            double barHeight = item.Count == 0
                ? 1
                : Math.Max(1, plot.Height * item.Count / maxValue);
            double x = plot.Left + (slotWidth * i) + ((slotWidth - barWidth) / 2);
            double y = plot.Top + plot.Height - barHeight;
            string color = DefectKnowledgeBase.TryGetInfo(item.Class, out DefectClassInfo? info)
                ? info.ColorHex
                : "#808080";

            svg.Append("<rect x=\"").Append(Num(x))
                .Append("\" y=\"").Append(Num(y))
                .Append("\" width=\"").Append(Num(barWidth))
                .Append("\" height=\"").Append(Num(barHeight))
                .Append("\" fill=\"").Append(color)
                .Append("\" stroke=\"#8A8A8A\" stroke-width=\"0.5\"/>");

            AppendText(
                svg,
                item.Abbreviation,
                plot.Left + (slotWidth * i) + (slotWidth / 2),
                plot.Top + plot.Height + 15,
                TextColor,
                FontSize,
                "middle");
        }

        return End(svg);
    }

    private static void AppendSeries(
        StringBuilder svg,
        IReadOnlyList<int> values,
        string color,
        int maxValue,
        Plot plot)
    {
        if (values.Count > 1)
        {
            svg.Append("<polyline fill=\"none\" stroke=\"").Append(color)
                .Append("\" stroke-width=\"2\" stroke-linejoin=\"round\" points=\"");
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0)
                {
                    svg.Append(' ');
                }

                svg.Append(Num(PointX(i, values.Count, plot))).Append(',')
                    .Append(Num(ValueY(values[i], maxValue, plot)));
            }

            svg.Append("\"/>");
        }

        for (int i = 0; i < values.Count; i++)
        {
            AppendCircle(
                svg,
                PointX(i, values.Count, plot),
                ValueY(values[i], maxValue, plot),
                2.5,
                color);
        }
    }

    private static void AppendXLabels(
        StringBuilder svg,
        IReadOnlyList<string> labels,
        int count,
        Plot plot)
    {
        int step = CalculateLabelStep(count, plot.Width);
        for (int i = 0; i < labels.Count; i++)
        {
            if (i % step != 0 && i != labels.Count - 1)
            {
                continue;
            }

            AppendText(
                svg,
                labels[i],
                PointX(i, count, plot),
                plot.Top + plot.Height + 15,
                TextColor,
                FontSize,
                "middle");
        }
    }

    private static void AppendAxes(StringBuilder svg, Plot plot)
    {
        for (int i = 0; i <= 3; i++)
        {
            double y = plot.Top + (plot.Height * i / 3.0);
            AppendLine(svg, plot.Left, y, plot.Left + plot.Width, y, i == 3 ? AxisColor : GridColor);
        }

        AppendLine(svg, plot.Left, plot.Top, plot.Left, plot.Top + plot.Height, AxisColor);
    }

    private static void AppendLegend(StringBuilder svg, Plot plot)
    {
        double x = plot.Left + plot.Width - 78;
        svg.Append("<rect x=\"").Append(Num(x)).Append("\" y=\"2\" width=\"8\" height=\"8\" fill=\"")
            .Append(PassColor).Append("\"/>");
        AppendText(svg, "PASS", x + 12, 10, TextColor, 8, "start");
        svg.Append("<rect x=\"").Append(Num(x + 42)).Append("\" y=\"2\" width=\"8\" height=\"8\" fill=\"")
            .Append(FailColor).Append("\"/>");
        AppendText(svg, "FAIL", x + 54, 10, TextColor, 8, "start");
    }

    private static void AppendCenteredText(StringBuilder svg, string text, Plot plot, double height)
    {
        AppendText(
            svg,
            text,
            plot.Left + (plot.Width / 2),
            plot.Top + (plot.Height / 2) + 4,
            TextColor,
            11,
            "middle");
    }

    private static void AppendCircle(
        StringBuilder svg,
        double x,
        double y,
        double radius,
        string fill)
    {
        svg.Append("<circle cx=\"").Append(Num(x))
            .Append("\" cy=\"").Append(Num(y))
            .Append("\" r=\"").Append(Num(radius))
            .Append("\" fill=\"").Append(fill).Append("\"/>");
    }

    private static void AppendLine(
        StringBuilder svg,
        double x1,
        double y1,
        double x2,
        double y2,
        string stroke)
    {
        svg.Append("<line x1=\"").Append(Num(x1))
            .Append("\" y1=\"").Append(Num(y1))
            .Append("\" x2=\"").Append(Num(x2))
            .Append("\" y2=\"").Append(Num(y2))
            .Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"1\"/>");
    }

    private static void AppendText(
        StringBuilder svg,
        string text,
        double x,
        double y,
        string fill,
        double fontSize,
        string anchor)
    {
        svg.Append("<text x=\"").Append(Num(x))
            .Append("\" y=\"").Append(Num(y))
            .Append("\" fill=\"").Append(fill)
            .Append("\" font-family=\"Segoe UI, sans-serif\" font-size=\"").Append(Num(fontSize))
            .Append("\" text-anchor=\"").Append(anchor).Append("\">")
            .Append(SecurityElement.Escape(text) ?? string.Empty)
            .Append("</text>");
    }

    private static StringBuilder Begin(double width, double height)
    {
        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(Num(width))
            .Append("\" height=\"").Append(Num(height))
            .Append("\" viewBox=\"0 0 ").Append(Num(width)).Append(' ').Append(Num(height))
            .Append("\">");
        return svg;
    }

    private static string End(StringBuilder svg) => svg.Append("</svg>").ToString();

    private static Plot CreatePlot(double width, double height)
    {
        double left = width < 260 ? 30 : 38;
        double right = 12;
        double top = 12;
        double bottom = height > 80 ? 26 : 12;
        return new Plot(
            left,
            top,
            Math.Max(1, width - left - right),
            Math.Max(1, height - top - bottom));
    }

    private static double PointX(int index, int count, Plot plot) =>
        count <= 1
            ? plot.Left + (plot.Width / 2)
            : plot.Left + (plot.Width * index / (count - 1.0));

    private static double ValueY(int value, int maxValue, Plot plot) =>
        plot.Top + (plot.Height * (1.0 - ((double)value / maxValue)));

    private static void ValidateDimensions(double width, double height)
    {
        if (!double.IsFinite(width) || width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Chart width must be positive.");
        }

        if (!double.IsFinite(height) || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Chart height must be positive.");
        }
    }

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private readonly record struct Plot(double Left, double Top, double Width, double Height);
}
