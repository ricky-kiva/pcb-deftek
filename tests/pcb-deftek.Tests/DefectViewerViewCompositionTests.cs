using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;
using pcb_deftek.Presentation.Controls;
using pcb_deftek.Presentation.Services;
using pcb_deftek.Presentation.ViewModels;
using pcb_deftek.Presentation.Views;
using Xunit;

namespace pcb_deftek.Tests;

public class DefectViewerViewCompositionTests
{
    [Fact]
    public void WideImage_IsFullyVisibleOnInitialLayout()
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                var viewModel = new InspectionViewerViewModel(
                    new Mock<IInspectionRepository>().Object,
                    new Mock<IViewportCropService>().Object,
                    new Mock<IInspectionDeletionService>().Object,
                    new Mock<IConfirmationService>().Object,
                    new Mock<INotificationService>().Object,
                    NullLogger<InspectionViewerViewModel>.Instance)
                {
                    ImageSource = CreateBitmap(400, 100)
                };

                var view = new DefectViewerView { DataContext = viewModel };
                view.Measure(new Size(1200, 800));
                view.Arrange(new Rect(0, 0, 1200, 800));
                view.UpdateLayout();

                var canvas = (DefectImageViewer)view.FindName("ImageCanvas");
                canvas.Transform.Should().NotBeNull();
                canvas.VisibleRegion.Should().Be(pcb_deftek.Domain.Models.NormalizedRect.Full);
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
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(captured).Throw();
        }
    }

    [Fact]
    public void ImageLoadedWhileCollapsed_IsFullyVisibleWhenShown()
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                var viewModel = new InspectionViewerViewModel(
                    new Mock<IInspectionRepository>().Object,
                    new Mock<IViewportCropService>().Object,
                    new Mock<IInspectionDeletionService>().Object,
                    new Mock<IConfirmationService>().Object,
                    new Mock<INotificationService>().Object,
                    NullLogger<InspectionViewerViewModel>.Instance);
                var view = new DefectViewerView
                {
                    DataContext = viewModel,
                    Visibility = Visibility.Collapsed
                };
                view.Measure(new Size(0, 0));
                view.Arrange(new Rect(0, 0, 0, 0));

                viewModel.ImageSource = CreateBitmap(400, 100);
                view.Visibility = Visibility.Visible;
                view.Measure(new Size(1200, 800));
                view.Arrange(new Rect(0, 0, 1200, 800));
                view.UpdateLayout();

                var canvas = (DefectImageViewer)view.FindName("ImageCanvas");
                canvas.Transform.Should().NotBeNull();
                canvas.VisibleRegion.Should().Be(pcb_deftek.Domain.Models.NormalizedRect.Full);
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
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(captured).Throw();
        }
    }

    private static BitmapSource CreateBitmap(int width, int height)
    {
        int stride = width * 4;
        var pixels = new byte[stride * height];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 120;
            pixels[i + 1] = 120;
            pixels[i + 2] = 120;
            pixels[i + 3] = 255;
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();
        return bitmap;
    }
}
