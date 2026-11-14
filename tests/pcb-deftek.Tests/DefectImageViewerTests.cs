using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluentAssertions;
using pcb_deftek.Domain.Models;
using pcb_deftek.Presentation.Controls;
using pcb_deftek.Presentation.ViewModels;
using Xunit;

namespace pcb_deftek.Tests;

public class DefectImageViewerTests
{
    [Fact]
    public void FocusAndReset_AnimateTheViewport()
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                var viewer = new DefectImageViewer { Width = 800, Height = 600 };
                viewer.ImageSource = CreateBitmap(200, 100);
                var marker = new DefectMarkerViewModel(
                    new DetectedDefect(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        "short",
                        0.9f,
                        new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f)),
                    new Rect(20, 10, 40, 20));
                viewer.Markers = new[] { marker };

                viewer.Measure(new Size(800, 600));
                viewer.Arrange(new Rect(0, 0, 800, 600));
                viewer.UpdateLayout();
                viewer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

                viewer.Transform.Should().NotBeNull();
                viewer.Transform!.ZoomFactor.Should().BeApproximately(1, 0.0001);

                viewer.SelectedMarker = marker;
                PumpDispatcher(TimeSpan.FromMilliseconds(300));

                marker.IsSelected.Should().BeTrue();
                viewer.Transform!.ZoomFactor.Should().BeGreaterThan(1);

                viewer.ResetView();
                PumpDispatcher(TimeSpan.FromMilliseconds(300));

                viewer.Transform!.ZoomFactor.Should().BeApproximately(1, 0.0001);

                viewer.ImageSource = CreateBitmap(400, 200);
                viewer.UpdateLayout();
                viewer.Transform!.ImageWidth.Should().Be(400);
                viewer.Transform.ImageHeight.Should().Be(200);
            }
            catch (Exception ex)
            {
                captured = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(captured);
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

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
