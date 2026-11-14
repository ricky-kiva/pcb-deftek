using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Presentation.Services;
using pcb_deftek.Presentation.ViewModels;
using pcb_deftek.Presentation.Views;
using Xunit;

namespace pcb_deftek.Tests;

public class DashboardViewCompositionTests
{
    [Fact]
    public void Dashboard_ComposesCardsFiltersAccordionAndStartupPreference()
    {
        RunOnSta(() =>
        {
            var (view, vm) = CreateView();
            view.DataContext = vm;
            vm.LoadAsync().GetAwaiter().GetResult();
            Layout(view);

            var cards = (ItemsControl)view.FindName("MetricCardsControl");
            cards.Items.Count.Should().Be(4);
            FindVisualChild<UniformGrid>(cards)!.Columns.Should().Be(2);

            vm.ToggleLayoutCommand.Execute(null);
            Layout(view);
            FindVisualChild<UniformGrid>(cards)!.Columns.Should().Be(1);

            ((ItemsControl)view.FindName("DefectInfoControl")).Items.Count.Should().Be(9);
            view.FindName("ClassFilterToggle").Should().NotBeNull();
            view.FindName("SeverityFilterToggle").Should().NotBeNull();
            view.FindName("CustomDatePopup").Should().NotBeNull();
            view.FindName("NotificationModalOverlay").Should().NotBeNull();
        });
    }

    [Fact]
    public void ProgressModal_BindsServiceStateAndCloses()
    {
        RunOnSta(() =>
        {
            var (view, vm) = CreateView();
            view.DataContext = vm;
            Layout(view);

            vm.OpenNotificationsCommand.Execute(null);
            vm.Progress.ReportInference(DashboardProgressStatus.Running, 42, "Detecting");
            Layout(view);

            ((Border)view.FindName("NotificationModalOverlay")).Visibility.Should().Be(Visibility.Visible);
            ((ProgressBar)view.FindName("InferenceProgressBar")).Value.Should().Be(42);

            vm.CloseNotificationsCommand.Execute(null);
            Layout(view);
            ((Border)view.FindName("NotificationModalOverlay")).Visibility.Should().Be(Visibility.Collapsed);
        });
    }

    [Fact]
    public void ProgressModal_RendersIdleRunningAndCompletedStates()
    {
        RunOnSta(() =>
        {
            var (view, vm) = CreateView();
            view.DataContext = vm;
            vm.LoadAsync().GetAwaiter().GetResult();
            vm.OpenNotificationsCommand.Execute(null);

            foreach (DashboardProgressStatus status in new[]
            {
                DashboardProgressStatus.Idle,
                DashboardProgressStatus.Running,
                DashboardProgressStatus.Completed
            })
            {
                vm.Progress.ReportInference(status, status == DashboardProgressStatus.Completed ? 100 : 40, status.ToString());
                vm.Progress.ReportDataExport(status, status == DashboardProgressStatus.Completed ? 100 : 25, status.ToString());
                RenderPixelCount(view, 1000, 720).Should().BeGreaterThan(0);
            }
        });
    }

    [Fact]
    public void Dashboard_OffscreenRendersAtWideAndNarrowWidths()
    {
        RunOnSta(() =>
        {
            var (view, vm) = CreateView();
            view.DataContext = vm;
            vm.LoadAsync().GetAwaiter().GetResult();

            RenderPixelCount(view, 1100, 720).Should().BeGreaterThan(0);
            RenderPixelCount(view, 820, 720).Should().BeGreaterThan(0);
        });
    }

    private static (DashboardView View, DashboardViewModel ViewModel) CreateView()
    {
        var repository = new FakeDashboardRepository();
        var vm = new DashboardViewModel(
            repository,
            new FakeSettingsService(),
            new DashboardProgressService());
        return (new DashboardView(), vm);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            T? nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static void Layout(FrameworkElement view)
    {
        view.Measure(new Size(1100, 720));
        view.Arrange(new Rect(0, 0, 1100, 720));
        view.UpdateLayout();
    }

    private static int RenderPixelCount(FrameworkElement view, double width, double height)
    {
        using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("dashboard-view-test")
        {
            Width = (int)width,
            Height = (int)height,
            WindowStyle = 0x800000
        });
        source.RootVisual = view;
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();

        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels.Where((_, index) => index % 4 == 3).Count(alpha => alpha > 0);
    }

    private sealed class FakeDashboardRepository : IDashboardRepository
    {
        public Task<DashboardStatistics> GetStatisticsAsync(
            DashboardFilter filter,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DashboardStatistics(
                DashboardBucketGranularity.Day,
                new[]
                {
                    new DashboardBucket(
                        DateTime.Today.AddDays(-1), DateTime.Today, "Yesterday", 3, 1, 2, 1),
                    new DashboardBucket(
                        DateTime.Today, DateTime.Today.AddDays(1), "Today", 2, 1, 1, 1)
                },
                DefectKnowledgeBase.GetAll()
                    .OrderBy(info => info.ClassId)
                    .Select(info => new DashboardClassCount(info.Class, info.Abbreviation, info.ClassId == 0 ? 1 : 0))
                    .ToArray()));
    }

    private sealed class FakeSettingsService : IUserSettingsService
    {
        private UserSettings _settings = new();

        public Task<UserSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_settings);

        public Task SaveSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
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
