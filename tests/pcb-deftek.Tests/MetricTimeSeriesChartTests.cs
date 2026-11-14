using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using pcb_deftek.Presentation.Controls;
using pcb_deftek.Presentation.ViewModels;
using Xunit;

namespace pcb_deftek.Tests;

public class MetricTimeSeriesChartTests
{
    [Fact]
    public void EmptySeries_RendersNoDataBaseline()
    {
        RunOnSta(() =>
        {
            var chart = Arrange(new MetricTimeSeriesChart(), Array.Empty<DashboardTimeSeriesPoint>());

            chart.RenderedPointCount.Should().Be(0);
            chart.RenderedSeriesCount.Should().Be(0);
            chart.IsEmptyStateVisible.Should().BeTrue();
            chart.AccessibleSummaryText.Should().Contain("No data");
        });
    }

    [Fact]
    public void OnePoint_RendersSingleVisiblePoint()
    {
        RunOnSta(() =>
        {
            var chart = Arrange(
                new MetricTimeSeriesChart(),
                new[] { new DashboardTimeSeriesPoint("Today", 9) });

            chart.RenderedPointCount.Should().Be(1);
            chart.RenderedSeriesCount.Should().Be(1);
            chart.RenderedLabelCount.Should().Be(1);
            chart.IsEmptyStateVisible.Should().BeFalse();
        });
    }

    [Fact]
    public void ManyPoints_ThinLabelsAndUseSeriesMetadata()
    {
        RunOnSta(() =>
        {
            var points = Enumerable.Range(0, 18)
                .Select(index => new DashboardTimeSeriesPoint($"D{index + 1}", index))
                .ToArray();
            var chart = new MetricTimeSeriesChart
            {
                Width = 320,
                Height = 180,
                SeriesName = "PCBs scanned",
                GranularityLabel = "Daily",
                SeriesBrush = Brushes.Orange
            };

            Arrange(chart, points);

            chart.LabelStep.Should().BeGreaterThan(1);
            chart.RenderedLabelCount.Should().BeLessThan(points.Length);
            chart.AccessibleSummaryText.Should().Contain("PCBs scanned").And.Contain("Daily").And.Contain("153");
        });
    }

    [Fact]
    public void HoverAndActivation_ExposeCountAndRaiseClickEvent()
    {
        RunOnSta(() =>
        {
            var points = new[]
            {
                new DashboardTimeSeriesPoint("1 Nov", 4),
                new DashboardTimeSeriesPoint("2 Nov", 7)
            };
            var chart = Arrange(new MetricTimeSeriesChart(), points);
            int activations = 0;
            chart.ChartActivated += (_, _) => activations++;

            chart.ShowHoverForTest(1);
            chart.ActivateForTest();

            chart.TooltipTextValue.Should().Contain("2 Nov").And.Contain("7");
            activations.Should().Be(1);
        });
    }

    [Fact]
    public void OffscreenRender_AtCardAndModalWidthsProducesPixels()
    {
        RunOnSta(() =>
        {
            var points = new[]
            {
                new DashboardTimeSeriesPoint("D1", 2),
                new DashboardTimeSeriesPoint("D2", 5),
                new DashboardTimeSeriesPoint("D3", 1)
            };

            RenderPixelCount(new MetricTimeSeriesChart(), points, 320, 180).Should().BeGreaterThan(0);
            RenderPixelCount(new MetricTimeSeriesChart(), points, 760, 320).Should().BeGreaterThan(0);
        });
    }

    private static MetricTimeSeriesChart Arrange(
        MetricTimeSeriesChart chart,
        IEnumerable<DashboardTimeSeriesPoint> points)
    {
        chart.Width = chart.Width > 0 ? chart.Width : 600;
        chart.Height = chart.Height > 0 ? chart.Height : 220;
        chart.Points = points;
        chart.Measure(new Size(chart.Width, chart.Height));
        chart.Arrange(new Rect(0, 0, chart.Width, chart.Height));
        chart.UpdateLayout();
        chart.PrepareForTest();
        return chart;
    }

    private static int RenderPixelCount(
        MetricTimeSeriesChart chart,
        IEnumerable<DashboardTimeSeriesPoint> points,
        double width,
        double height)
    {
        Arrange(chart, points);
        using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("chart-test")
        {
            Width = (int)width,
            Height = (int)height,
            WindowStyle = 0x800000
        });
        source.RootVisual = chart;
        chart.Measure(new Size(width, height));
        chart.Arrange(new Rect(0, 0, width, height));
        chart.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            (int)width,
            (int)height,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(chart);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels.Where((_, index) => index % 4 == 3).Count(alpha => alpha > 0);
    }

    private static void RunOnSta(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
            finally
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (captured is not null)
        {
            throw new InvalidOperationException("STA test execution failed.", captured);
        }
    }
}
