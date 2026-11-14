using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using pcb_deftek.Domain.Models;
using pcb_deftek.Presentation.Models;
using pcb_deftek.Presentation.ViewModels;

namespace pcb_deftek.Presentation.Controls;

/// <summary>
/// Renders a PCB image and its defect markers in one transformed layer, with
/// cursor-anchored zoom, clamped drag panning, marker hit-testing, and animated
/// focus/reset transitions.
/// </summary>
public partial class DefectImageViewer : UserControl
{
    private const double DragThreshold = 4;
    private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(180);

    private ViewportTransform? _transform;
    private DispatcherTimer? _animationTimer;
    private Stopwatch? _animationClock;
    private double _fromScale;
    private double _fromOffsetX;
    private double _fromOffsetY;
    private double _toScale;
    private double _toOffsetX;
    private double _toOffsetY;
    private Point _dragStart;
    private Point _dragLast;
    private bool _isPanning;
    private bool _isDragging;

    public DefectImageViewer()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets or sets the full-size image rendered by the viewer.
    /// </summary>
    public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.Register(
        nameof(ImageSource),
        typeof(BitmapSource),
        typeof(DefectImageViewer),
        new PropertyMetadata(null, OnImageSourceChanged));

    /// <summary>
    /// Gets or sets the defect markers to overlay on the image.
    /// </summary>
    public static readonly DependencyProperty MarkersProperty = DependencyProperty.Register(
        nameof(Markers),
        typeof(IEnumerable<DefectMarkerViewModel>),
        typeof(DefectImageViewer),
        new PropertyMetadata(null, OnMarkersChanged));

    /// <summary>
    /// Gets or sets the focused marker. Setting it animates the viewer toward that defect.
    /// </summary>
    public static readonly DependencyProperty SelectedMarkerProperty = DependencyProperty.Register(
        nameof(SelectedMarker),
        typeof(DefectMarkerViewModel),
        typeof(DefectImageViewer),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectedMarkerChanged));

    private static readonly DependencyPropertyKey TransformPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Transform),
        typeof(ViewportTransform),
        typeof(DefectImageViewer),
        new PropertyMetadata(null));

    /// <summary>
    /// Gets the current viewport transform. The control owns this instance.
    /// </summary>
    public static readonly DependencyProperty TransformProperty = TransformPropertyKey.DependencyProperty;

    /// <summary>
    /// Raised when a marker is clicked, or when empty image space clears the focus.
    /// </summary>
    public event EventHandler<DefectMarkerViewModel?>? MarkerSelected;

    /// <summary>
    /// Raised whenever the visible image region changes.
    /// </summary>
    public event EventHandler<NormalizedRect>? ViewportChanged;

    public BitmapSource? ImageSource
    {
        get => (BitmapSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public IEnumerable<DefectMarkerViewModel>? Markers
    {
        get => (IEnumerable<DefectMarkerViewModel>?)GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    public DefectMarkerViewModel? SelectedMarker
    {
        get => (DefectMarkerViewModel?)GetValue(SelectedMarkerProperty);
        set => SetCurrentValue(SelectedMarkerProperty, value);
    }

    public ViewportTransform? Transform => (ViewportTransform?)GetValue(TransformProperty);

    /// <summary>Gets the currently visible normalized image region.</summary>
    public NormalizedRect VisibleRegion => Transform?.GetVisibleNormalizedRect() ?? NormalizedRect.Empty;

    /// <summary>Animates back to the exact centered fit.</summary>
    public void ResetView()
    {
        if (_transform is null)
        {
            return;
        }

        var target = new ViewportTransform(
            _transform.ImageWidth,
            _transform.ImageHeight,
            _transform.ViewportWidth,
            _transform.ViewportHeight);

        AnimateTo(target.Scale, target.OffsetX, target.OffsetY);
    }

    private static void OnImageSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((DefectImageViewer)d).ApplyImageSource(e.NewValue as BitmapSource);
    }

    private static void OnMarkersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((DefectImageViewer)d).UpdateMarkerSelection();
    }

    private static void OnSelectedMarkerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var viewer = (DefectImageViewer)d;
        viewer.UpdateMarkerSelection();

        if (e.NewValue is DefectMarkerViewModel marker)
        {
            viewer.FocusMarker(marker);
        }
    }

    private void ApplyImageSource(BitmapSource? image)
    {
        StopAnimation();
        BoardImage.Source = image;

        if (image is null || image.PixelWidth <= 0 || image.PixelHeight <= 0)
        {
            _transform = null;
            Scene.Width = 0;
            Scene.Height = 0;
            SetValue(TransformPropertyKey, null);
            return;
        }

        Scene.Width = image.PixelWidth;
        Scene.Height = image.PixelHeight;
        EnsureTransform();
    }

    private void EnsureTransform()
    {
        if (ImageSource is not { PixelWidth: > 0, PixelHeight: > 0 } image)
        {
            return;
        }

        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        if (_transform is null ||
            _transform.ImageWidth != image.PixelWidth ||
            _transform.ImageHeight != image.PixelHeight)
        {
            _transform = new ViewportTransform(
                image.PixelWidth,
                image.PixelHeight,
                ActualWidth,
                ActualHeight);
        }
        else
        {
            _transform.Resize(ActualWidth, ActualHeight);
        }

        ApplyTransform();

        if (SelectedMarker is { } selected)
        {
            FocusMarker(selected);
        }
    }

    private void ApplyTransform()
    {
        if (_transform is null)
        {
            return;
        }

        SceneScale.ScaleX = _transform.Scale;
        SceneScale.ScaleY = _transform.Scale;
        SceneTranslate.X = _transform.OffsetX;
        SceneTranslate.Y = _transform.OffsetY;
        SetValue(TransformPropertyKey, _transform);
        ViewportChanged?.Invoke(this, _transform.GetVisibleNormalizedRect());
    }

    private void UpdateMarkerSelection()
    {
        if (Markers is null)
        {
            return;
        }

        foreach (var marker in Markers)
        {
            marker.IsSelected = ReferenceEquals(marker, SelectedMarker);
        }
    }

    private void FocusMarker(DefectMarkerViewModel marker)
    {
        if (_transform is null || marker.PixelBounds.IsEmpty)
        {
            return;
        }

        var target = new ViewportTransform(
            _transform.ImageWidth,
            _transform.ImageHeight,
            _transform.ViewportWidth,
            _transform.ViewportHeight);
        target.FocusOn(new NormalizedRect(
            marker.NormalizedBounds.X,
            marker.NormalizedBounds.Y,
            marker.NormalizedBounds.Width,
            marker.NormalizedBounds.Height));

        AnimateTo(target.Scale, target.OffsetX, target.OffsetY);
    }

    private void AnimateTo(double scale, double offsetX, double offsetY)
    {
        if (_transform is null)
        {
            return;
        }

        StopAnimation();

        _fromScale = _transform.Scale;
        _fromOffsetX = _transform.OffsetX;
        _fromOffsetY = _transform.OffsetY;
        _toScale = scale;
        _toOffsetX = offsetX;
        _toOffsetY = offsetY;

        _animationClock = Stopwatch.StartNew();
        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _animationTimer.Tick += OnAnimationTick;
        _animationTimer.Start();
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (_transform is null || _animationClock is null)
        {
            StopAnimation();
            return;
        }

        double progress = Math.Clamp(
            _animationClock.Elapsed.TotalMilliseconds / AnimationDuration.TotalMilliseconds,
            0,
            1);
        double eased = 1 - Math.Pow(1 - progress, 3);

        _transform.SetView(
            Lerp(_fromScale, _toScale, eased),
            Lerp(_fromOffsetX, _toOffsetX, eased),
            Lerp(_fromOffsetY, _toOffsetY, eased));
        ApplyTransform();

        if (progress >= 1)
        {
            StopAnimation();
        }
    }

    private void StopAnimation()
    {
        if (_animationTimer is not null)
        {
            _animationTimer.Stop();
            _animationTimer.Tick -= OnAnimationTick;
            _animationTimer = null;
        }

        _animationClock = null;
    }

    private static double Lerp(double from, double to, double progress) =>
        from + ((to - from) * progress);

    private DefectMarkerViewModel? HitTest(Point point)
    {
        if (_transform is null || Markers is null)
        {
            return null;
        }

        double imageX = (point.X - _transform.OffsetX) / _transform.Scale;
        double imageY = (point.Y - _transform.OffsetY) / _transform.Scale;

        return Markers
            .Reverse()
            .FirstOrDefault(marker =>
                !marker.PixelBounds.IsEmpty &&
                marker.PixelBounds.Contains(imageX, imageY));
    }

    private void OnViewerLoaded(object sender, RoutedEventArgs e)
    {
        EnsureTransform();
    }

    private void OnViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
        {
            return;
        }

        StopAnimation();
        EnsureTransform();
    }

    private void OnViewerMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_transform is null)
        {
            return;
        }

        StopAnimation();
        var position = e.GetPosition(ViewportSurface);
        _transform.ZoomAt(position.X, position.Y, Math.Pow(1.1, e.Delta / 120.0));
        ApplyTransform();
        UpdateCursor(position);
        e.Handled = true;
    }

    private void OnViewerMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_transform is null)
        {
            return;
        }

        Focus();
        _isPanning = true;
        _isDragging = false;
        _dragStart = e.GetPosition(ViewportSurface);
        _dragLast = _dragStart;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnViewerMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
        {
            UpdateCursor(e.GetPosition(ViewportSurface));
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag();
            return;
        }

        Point position = e.GetPosition(ViewportSurface);
        if (!_isDragging)
        {
            Vector deltaFromStart = position - _dragStart;
            if (Math.Abs(deltaFromStart.X) >= DragThreshold || Math.Abs(deltaFromStart.Y) >= DragThreshold)
            {
                _isDragging = true;
            }
        }

        if (_isDragging && _transform is not null)
        {
            StopAnimation();
            Vector delta = position - _dragLast;
            _transform.Pan(delta.X, delta.Y);
            ApplyTransform();
            Cursor = Cursors.SizeAll;
        }

        _dragLast = position;
        e.Handled = true;
    }

    private void OnViewerMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        Point position = e.GetPosition(ViewportSurface);
        bool wasDragging = _isDragging;
        EndDrag();

        if (!wasDragging)
        {
            var marker = HitTest(position);
            SetCurrentValue(SelectedMarkerProperty, marker);
            MarkerSelected?.Invoke(this, marker);
        }

        UpdateCursor(position);
        e.Handled = true;
    }

    private void OnViewerLostMouseCapture(object sender, MouseEventArgs e)
    {
        _isPanning = false;
        _isDragging = false;
    }

    private void OnViewerMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
        {
            Cursor = Cursors.Arrow;
        }
    }

    private void EndDrag()
    {
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        _isPanning = false;
        _isDragging = false;
    }

    private void UpdateCursor(Point position)
    {
        if (_isDragging)
        {
            Cursor = Cursors.SizeAll;
            return;
        }

        Cursor = HitTest(position) is null ? Cursors.Arrow : Cursors.Hand;
    }
}
