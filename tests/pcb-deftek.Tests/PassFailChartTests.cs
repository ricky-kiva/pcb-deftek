using System;
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

public class PassFailChartTests
{
    [Fact]
    public void EmptySeries_RendersNoDataStateWithoutPoints()
    {
        RunOnSta(() =>
        {
            var chart = Arrange(new PassFailLineChart(), Array.Empty<DashboardPassFailPoint>());

            chart.RenderedPointCount.Should().Be(0);
            chart.RenderedSeriesCount.Should().Be(0);
            chart.IsEmptyStateVisible.Should().BeTrue();
            chart.AccessibleSummaryText.Should().Contain("No data");
        });
    }

    [Fact]
    public void OnePoint_RendersBothSeriesMarkersAndOneLabel()
    {
        RunOnSta(() =>
        {
            var chart = Arrange(
                new PassFailLineChart(),
                new[] { new DashboardPassFailPoint("Today", 4, 1) });

            chart.RenderedPointCount.Should().Be(2);
            chart.RenderedSeriesCount.Should().Be(2);
            chart.RenderedLabelCount.Should().Be(1);
            chart.IsEmptyStateVisible.Should().BeFalse();
        });
    }

    [Fact]
    public void ManyPoints_ThinLabelsAtNarrowWidth()
    {
        RunOnSta(() =>
        {
            var points = Enumerable.Range(0, 12)
                .Select(index => new DashboardPassFailPoint($"D{index + 1}", index, 12 - index))
                .ToArray();
            var chart = new PassFailLineChart { Width = 300, Height = 180 };

            Arrange(chart, points);

            chart.LabelStep.Should().BeGreaterThan(1);
            chart.RenderedLabelCount.Should().BeLessThan(points.Length);
            chart.RenderedLabelCount.Should().BeGreaterThan(1);
            chart.AccessibleSummaryText.Should().Contain("PASS").And.Contain("FAIL");
        });
    }

    [Fact]
    public void HoverAndActivation_ExposeValuesAndRaiseClickEvent()
    {
        RunOnSta(() =>
        {
            var points = new[]
            {
                new DashboardPassFailPoint("1 Nov", 4, 1),
                new DashboardPassFailPoint("2 Nov", 2, 3)
            };
            var chart = Arrange(new PassFailLineChart(), points);
            int activations = 0;
            chart.ChartActivated += (_, _) => activations++;

            chart.ShowHoverForTest(1);
            chart.ActivateForTest();

            chart.TooltipTextValue.Should().Contain("2 Nov").And.Contain("2 PASS").And.Contain("3 FAIL");
            activations.Should().Be(1);
        });
    }

    [Fact]
    public void OffscreenRender_AtCardAndModalWidthsProducesPixels()
    {
        RunOnSta(() =>
        {
            var points = Enumerable.Range(0, 6)
                .Select(index => new DashboardPassFailPoint($"D{index}", index + 1, 2))
                .ToArray();

            RenderPixelCount(new PassFailLineChart(), points, 320, 180).Should().BeGreaterThan(0);
            RenderPixelCount(new PassFailLineChart(), points, 760, 320).Should().BeGreaterThan(0);
        });
    }

    private static PassFailLineChart Arrange(PassFailLineChart chart, System.Collections.Generic.IEnumerable<DashboardPassFailPoint> points)
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
        PassFailLineChart chart,
        System.Collections.Generic.IEnumerable<DashboardPassFailPoint> points,
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
