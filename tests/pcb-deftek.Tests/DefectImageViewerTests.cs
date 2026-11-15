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

    [Fact]
    public void ReplacingTheImageResetsZoomToTheFullFit()
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                var viewer = new DefectImageViewer { Width = 800, Height = 600 };
                viewer.ImageSource = CreateBitmap(1024, 1024);
                viewer.Measure(new Size(800, 600));
                viewer.Arrange(new Rect(0, 0, 800, 600));
                viewer.UpdateLayout();
                viewer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

                var marker = new DefectMarkerViewModel(
                    new DetectedDefect(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        "short",
                        0.9f,
                        new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f)),
                    new Rect(40, 10, 80, 20));
                viewer.Markers = new[] { marker };
                viewer.SelectedMarker = marker;
                PumpDispatcher(TimeSpan.FromMilliseconds(300));
                viewer.Transform!.ZoomFactor.Should().BeGreaterThan(1);

                viewer.SelectedMarker = null;
                viewer.Markers = null;
                viewer.ImageSource = CreateBitmap(1024, 1024);
                viewer.UpdateLayout();

                viewer.Transform!.ZoomFactor.Should().BeApproximately(1, 0.0001);
                viewer.VisibleRegion.Should().Be(pcb_deftek.Domain.Models.NormalizedRect.Full);
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

    [Fact]
    public void InitialFitShowsTheEntireNonSquareImage()
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                var viewer = new DefectImageViewer { Width = 800, Height = 600 };
                viewer.ImageSource = CreateBitmap(400, 100);
                viewer.Measure(new Size(800, 600));
                viewer.Arrange(new Rect(0, 0, 800, 600));
                viewer.UpdateLayout();
                viewer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

                viewer.Transform.Should().NotBeNull();
                viewer.Transform!.Scale.Should().BeApproximately(2, 0.0001);
                viewer.Transform.DisplayWidth.Should().BeApproximately(800, 0.0001);
                viewer.Transform.DisplayHeight.Should().BeApproximately(200, 0.0001);
                viewer.Transform.OffsetX.Should().BeApproximately(0, 0.0001);
                viewer.Transform.OffsetY.Should().BeApproximately(200, 0.0001);
                viewer.VisibleRegion.Should().Be(NormalizedRect.Full);
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

    [Fact]
    public void RenderedImageShowsAllFourQuadrantsWhenScaledToFit()
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                var viewer = new DefectImageViewer { Width = 400, Height = 400 };
                viewer.ImageSource = CreateQuadrantBitmap(800);
                viewer.Measure(new Size(400, 400));
                viewer.Arrange(new Rect(0, 0, 400, 400));
                viewer.UpdateLayout();
                viewer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

                var render = new RenderTargetBitmap(400, 400, 96, 96, PixelFormats.Pbgra32);
                render.Render(viewer);

                var pixels = new byte[400 * 400 * 4];
                render.CopyPixels(pixels, 400 * 4, 0);

                assert(100, 100, 255, 0, 0);   // top-left red
                assert(300, 100, 0, 128, 0);   // top-right green
                assert(100, 300, 0, 0, 255);   // bottom-left blue
                assert(300, 300, 255, 255, 0); // bottom-right yellow

                void assert(int x, int y, byte r, byte g, byte b)
                {
                    int offset = ((y * 400) + x) * 4;
                    pixels[offset + 2].Should().Be(r);
                    pixels[offset + 1].Should().Be(g);
                    pixels[offset].Should().Be(b);
                }
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

    private static BitmapSource CreateQuadrantBitmap(int size)
    {
        int half = size / 2;
        int stride = size * 4;
        var pixels = new byte[stride * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int offset = ((y * stride) + (x * 4));
                (byte b, byte g, byte r) = (x < half, y < half) switch
                {
                    (true, true) => ((byte)0, (byte)0, (byte)255),
                    (false, true) => ((byte)0, (byte)128, (byte)0),
                    (true, false) => ((byte)255, (byte)0, (byte)0),
                    _ => ((byte)0, (byte)255, (byte)255)
                };
                pixels[offset] = b;
                pixels[offset + 1] = g;
                pixels[offset + 2] = r;
                pixels[offset + 3] = 255;
            }
        }

        var bitmap = BitmapSource.Create(
            size,
            size,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();
        return bitmap;
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
