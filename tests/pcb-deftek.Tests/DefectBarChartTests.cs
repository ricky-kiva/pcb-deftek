using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Presentation.Controls;
using Xunit;

namespace pcb_deftek.Tests;

public class DefectBarChartTests
{
    [Fact]
    public void EmptyItems_RenderNoDataState()
    {
        RunOnSta(() =>
        {
            var chart = Arrange(new DefectBarChart(), Array.Empty<DashboardClassCount>());

            chart.RenderedBarCount.Should().Be(0);
            chart.RenderedLabelCount.Should().Be(0);
            chart.IsEmptyStateVisible.Should().BeTrue();
            chart.AccessibleSummaryText.Should().Contain("No data");
        });
    }

    [Fact]
    public void NineClasses_RenderBarsLabelsAndClassColorsInOrder()
    {
        RunOnSta(() =>
        {
            var items = BuildItems();
            var chart = Arrange(new DefectBarChart(), items);

            chart.RenderedBarCount.Should().Be(9);
            chart.RenderedLabelCount.Should().Be(9);
            chart.IsEmptyStateVisible.Should().BeFalse();
            chart.RenderedBarColorHexes.Should().Equal(
                DefectKnowledgeBase.GetAll().OrderBy(info => info.ClassId).Select(info => info.ColorHex));
            chart.AccessibleSummaryText.Should().Contain("Short 3").And.Contain("Base material foreign object 0");
        });
    }

    [Fact]
    public void HoverAndActivation_ExposeClassNameAndCount()
    {
        RunOnSta(() =>
        {
            var chart = Arrange(new DefectBarChart(), BuildItems());
            int activations = 0;
            chart.ChartActivated += (_, _) => activations++;

            chart.ShowHoverForTest(0);
            chart.ActivateForTest();

            chart.TooltipTextValue.Should().Contain("Short").And.Contain("3");
            activations.Should().Be(1);
        });
    }

    [Fact]
    public void OffscreenRender_AtNarrowAndModalWidthsProducesPixels()
    {
        RunOnSta(() =>
        {
            RenderPixelCount(new DefectBarChart(), BuildItems(), 320, 180).Should().BeGreaterThan(0);
            RenderPixelCount(new DefectBarChart(), BuildItems(), 760, 320).Should().BeGreaterThan(0);
        });
    }

    private static DashboardClassCount[] BuildItems() =>
        DefectKnowledgeBase.GetAll()
            .OrderBy(info => info.ClassId)
            .Select((info, index) => new DashboardClassCount(
                info.Class,
                info.Abbreviation,
                index == 0 ? 3 : index == 1 ? 5 : 0))
            .ToArray();

    private static DefectBarChart Arrange(DefectBarChart chart, IEnumerable<DashboardClassCount> items)
    {
        chart.Width = chart.Width > 0 ? chart.Width : 600;
        chart.Height = chart.Height > 0 ? chart.Height : 220;
        chart.Items = items;
        chart.Measure(new Size(chart.Width, chart.Height));
        chart.Arrange(new Rect(0, 0, chart.Width, chart.Height));
        chart.UpdateLayout();
        chart.PrepareForTest();
        return chart;
    }

    private static int RenderPixelCount(
        DefectBarChart chart,
        IEnumerable<DashboardClassCount> items,
        double width,
        double height)
    {
        Arrange(chart, items);
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
