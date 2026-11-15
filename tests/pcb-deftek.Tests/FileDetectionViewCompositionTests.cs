using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using pcb_deftek.Infrastructure.Inference;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;
using pcb_deftek.Infrastructure.Vision;
using pcb_deftek.Presentation.ViewModels;
using pcb_deftek.Presentation.Views;
using Xunit;

namespace pcb_deftek.Tests;

public class FileDetectionViewCompositionTests
{
    [Fact]
    public void FileDetectionView_EmptyQueueIconAndPreviewModalMatchRevision()
    {
        RunOnSta(() =>
        {
            var (view, vm) = CreateView();
            view.Resources["TextOnAccentFillColorPrimaryBrush"] = new SolidColorBrush(Colors.White);
            view.Resources["TextOnAccentFillColorDisabledBrush"] = new SolidColorBrush(Colors.Gray);
            view.Resources["SolidBackgroundFillColorBaseBrush"] = new SolidColorBrush(Colors.Black);
            view.DataContext = vm;
            Layout(view);

            // Empty queue: the play glyph tracks the disabled button text foreground.
            var startButton = (Button)view.FindName("StartInspectionButton");
            var playIcon = (Path)view.FindName("StartInspectionPlayIcon");
            var label = (TextBlock)view.FindName("StartInspectionText");
            startButton.IsEnabled.Should().BeFalse();
            var iconBrush = playIcon.Fill.Should().BeOfType<SolidColorBrush>().Subject;
            var textBrush = label.Foreground.Should().BeOfType<SolidColorBrush>().Subject;
            iconBrush.Color.Should().Be(textBrush.Color);

            // Preview: opaque card/body and no Defect Details footer.
            var item = new FileDetectionQueueItemViewModel("board.png");
            vm.QueueItems.Add(item);
            vm.OpenPreviewCommand.Execute(item);

            var card = (Border)view.FindName("PreviewModalCard");
            var body = (Border)view.FindName("PreviewModalBody");
            ((SolidColorBrush)card.Background).Color.A.Should().Be(255);
            ((SolidColorBrush)body.Background).Color.A.Should().Be(255);
            FindVisualChildren<TextBlock>(view)
                .Should().NotContain(text => text.Text == "Defect Details:");
        });
    }

    private static (FileDetectionView View, FileDetectionViewModel ViewModel) CreateView()
    {
        var vm = new FileDetectionViewModel(
            new Mock<IFileDiscoveryService>().Object,
            new Mock<IFilePickerService>().Object,
            new Mock<IBatchInspectionEngine>().Object,
            new Mock<INotificationService>().Object,
            new Mock<IUserSettingsService>().Object,
            new Mock<IImageAnnotationService>().Object,
            NullLogger<FileDetectionViewModel>.Instance);
        return (new FileDetectionView(), vm);
    }

    private static void Layout(FrameworkElement view)
    {
        view.Measure(new Size(1200, 800));
        view.Arrange(new Rect(0, 0, 1200, 800));
        view.UpdateLayout();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
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
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (captured is not null)
        {
            ExceptionDispatchInfo.Capture(captured).Throw();
        }
    }
}
