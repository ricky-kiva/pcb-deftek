using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Moq;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Presentation.Controls;
using pcb_deftek.Presentation.Services;
using pcb_deftek.Presentation.ViewModels;
using pcb_deftek.Presentation.Views;
using Xunit;

namespace pcb_deftek.Tests;

public class DashboardChartHostTests
{
    [Fact]
    public void Host_RendersTheCardChartKindAndRaisesActivation()
    {
        RunOnSta(() =>
        {
            var card = BuildCard();
            var host = new DashboardChartHost { Card = card, Width = 640, Height = 240 };
            host.Measure(new Size(640, 240));
            host.Arrange(new Rect(0, 0, 640, 240));
            host.UpdateLayout();
            int activations = 0;
            host.ChartActivated += (_, _) => activations++;

            host.VisibleChartCount.Should().Be(1);
            host.Card.Should().BeSameAs(card);
            ((MetricTimeSeriesChart)host.FindName("MetricChart")).ActivateForTest();

            activations.Should().Be(1);
        });
    }

    [Fact]
    public void Host_DistributionUsesTheChartsNineClassSummary()
    {
        RunOnSta(() =>
        {
            var counts = DefectKnowledgeBase.GetAll()
                .OrderBy(info => info.ClassId)
                .Select(info => new DashboardClassCount(info.Class, info.Abbreviation, 1))
                .ToArray();
            var card = new DashboardMetricCardViewModel(
                DashboardCardOrderPolicy.Distribution,
                "Defect Distribution",
                "9",
                "All nine defect classes",
                DashboardMetricCardKind.Distribution,
                classCounts: counts,
                accessibleSummary: "9 defects distributed across 9 defect classes.");
            var host = new DashboardChartHost { Card = card, Width = 640, Height = 240 };
            host.Measure(new Size(640, 240));
            host.Arrange(new Rect(0, 0, 640, 240));
            host.UpdateLayout();

            var chart = (DefectBarChart)host.FindName("BarChart");
            chart.AccessibleSummaryText.Should().Contain("Short 1").And.Contain("Base material foreign object 1");
        });
    }

    [Fact]
    public void Modal_ProjectsTheSameCardAndClosesOnCommand()
    {
        RunOnSta(() =>
        {
            var vm = CreateViewModel();
            var view = new DashboardView { DataContext = vm };
            var card = BuildCard();
            view.Measure(new Size(1000, 700));
            view.Arrange(new Rect(0, 0, 1000, 700));
            view.UpdateLayout();

            vm.OpenChartCommand.Execute(card);
            view.UpdateLayout();

            var overlay = (Border)view.FindName("ChartModalOverlay");
            overlay.Visibility.Should().Be(Visibility.Visible);
            view.ModalHost.Card.Should().BeSameAs(card);

            vm.CloseChartCommand.Execute(null);
            view.UpdateLayout();

            overlay.Visibility.Should().Be(Visibility.Collapsed);
            view.ModalHost.Card.Should().BeNull();
        });
    }

    [Fact]
    public void Modal_OffscreenRenderAtEnlargedSizeProducesPixels()
    {
        RunOnSta(() =>
        {
            var vm = CreateViewModel();
            var view = new DashboardView { DataContext = vm };
            vm.OpenChartCommand.Execute(BuildCard());
            view.Measure(new Size(1000, 700));
            view.Arrange(new Rect(0, 0, 1000, 700));
            view.UpdateLayout();

            using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("dashboard-modal-test")
            {
                Width = 1000,
                Height = 700,
                WindowStyle = 0x800000
            });
            source.RootVisual = view;
            view.Measure(new Size(1000, 700));
            view.Arrange(new Rect(0, 0, 1000, 700));
            view.UpdateLayout();

            var bitmap = new RenderTargetBitmap(1000, 700, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            pixels.Where((_, index) => index % 4 == 3).Count(alpha => alpha > 0).Should().BeGreaterThan(0);
        });
    }

    private static DashboardMetricCardViewModel BuildCard() => new(
        DashboardCardOrderPolicy.TotalPcbs,
        "Total PCBs Inspected",
        "12",
        "Daily · 3 buckets",
        DashboardMetricCardKind.TimeSeries,
        timeSeries: new[]
        {
            new DashboardTimeSeriesPoint("1 Nov", 3),
            new DashboardTimeSeriesPoint("2 Nov", 5),
            new DashboardTimeSeriesPoint("3 Nov", 4)
        },
        seriesColorHex: "#0066FF",
        accessibleSummary: "12 PCBs scanned across 3 daily buckets.",
        granularityLabel: "Daily");

    private static DashboardViewModel CreateViewModel() => new(
        new Mock<IDashboardRepository>().Object,
        new Mock<IUserSettingsService>().Object,
        new DashboardProgressService());

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
