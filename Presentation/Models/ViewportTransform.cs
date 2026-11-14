using System;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Presentation.Models;

/// <summary>
/// Pure fit, zoom, pan, and focus geometry for the interactive defect viewer.
/// Offsets are the image origin in viewport coordinates; scale is viewport units per image pixel.
/// </summary>
public sealed class ViewportTransform
{
    /// <summary>Minimum zoom relative to the fit scale.</summary>
    public const double MinZoom = 0.5;

    /// <summary>Maximum zoom relative to the fit scale.</summary>
    public const double MaxZoom = 10.0;

    private const double Epsilon = 0.000001;

    public ViewportTransform(
        double imageWidth,
        double imageHeight,
        double viewportWidth,
        double viewportHeight)
    {
        if (!double.IsFinite(imageWidth) || imageWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(imageWidth));
        }

        if (!double.IsFinite(imageHeight) || imageHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(imageHeight));
        }

        if (!double.IsFinite(viewportWidth) || viewportWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        }

        if (!double.IsFinite(viewportHeight) || viewportHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportHeight));
        }

        ImageWidth = imageWidth;
        ImageHeight = imageHeight;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        FitScale = Math.Min(viewportWidth / imageWidth, viewportHeight / imageHeight);

        Reset();
    }

    /// <summary>Gets the source image width in pixels.</summary>
    public double ImageWidth { get; }

    /// <summary>Gets the source image height in pixels.</summary>
    public double ImageHeight { get; }

    /// <summary>Gets the viewport width in device-independent units.</summary>
    public double ViewportWidth { get; }

    /// <summary>Gets the viewport height in device-independent units.</summary>
    public double ViewportHeight { get; }

    /// <summary>Gets the scale that fits the whole image inside the viewport.</summary>
    public double FitScale { get; }

    /// <summary>Gets the current viewport units per image pixel.</summary>
    public double Scale { get; private set; }

    /// <summary>Gets the image origin X coordinate in viewport units.</summary>
    public double OffsetX { get; private set; }

    /// <summary>Gets the image origin Y coordinate in viewport units.</summary>
    public double OffsetY { get; private set; }

    /// <summary>Gets the current zoom relative to <see cref="FitScale"/>.</summary>
    public double ZoomFactor => FitScale <= 0 ? 1 : Scale / FitScale;

    /// <summary>Gets the displayed image width in viewport units.</summary>
    public double DisplayWidth => ImageWidth * Scale;

    /// <summary>Gets the displayed image height in viewport units.</summary>
    public double DisplayHeight => ImageHeight * Scale;

    /// <summary>Resets to the exact centered fit transform.</summary>
    public void Reset()
    {
        Scale = FitScale;
        OffsetX = (ViewportWidth - DisplayWidth) / 2;
        OffsetY = (ViewportHeight - DisplayHeight) / 2;
    }

    /// <summary>
    /// Zooms by a multiplicative factor while keeping the image point under the anchor stationary.
    /// The resulting zoom is clamped to <see cref="MinZoom"/>–<see cref="MaxZoom"/> of fit.
    /// </summary>
    public void ZoomAt(double viewportX, double viewportY, double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(factor));
        }

        double imageX = (viewportX - OffsetX) / Scale;
        double imageY = (viewportY - OffsetY) / Scale;
        double targetScale = Math.Clamp(Scale * factor, FitScale * MinZoom, FitScale * MaxZoom);

        if (Math.Abs(targetScale - Scale) <= Epsilon)
        {
            return;
        }

        Scale = targetScale;
        OffsetX = viewportX - (imageX * Scale);
        OffsetY = viewportY - (imageY * Scale);
        ClampOffsets();
    }

    /// <summary>
    /// Pans by a viewport-space delta and clamps the image so no empty space appears beyond its edges.
    /// </summary>
    public void Pan(double deltaX, double deltaY)
    {
        if (!double.IsFinite(deltaX))
        {
            throw new ArgumentOutOfRangeException(nameof(deltaX));
        }

        if (!double.IsFinite(deltaY))
        {
            throw new ArgumentOutOfRangeException(nameof(deltaY));
        }

        OffsetX += deltaX;
        OffsetY += deltaY;
        ClampOffsets();
    }

    /// <summary>
    /// Zooms and centers the specified normalized region, leaving a proportional padding margin.
    /// </summary>
    public void FocusOn(NormalizedRect region, double padding = 0.15)
    {
        if (region.IsEmpty)
        {
            return;
        }

        double clampedPadding = Math.Clamp(padding, 0, 0.45);
        double availableWidth = ViewportWidth * (1 - (2 * clampedPadding));
        double availableHeight = ViewportHeight * (1 - (2 * clampedPadding));

        double scaleX = availableWidth / (region.Width * ImageWidth);
        double scaleY = availableHeight / (region.Height * ImageHeight);
        double targetScale = Math.Clamp(
            Math.Min(scaleX, scaleY),
            FitScale * MinZoom,
            FitScale * MaxZoom);

        Scale = targetScale;
        double centerX = (region.X + (region.Width / 2)) * ImageWidth;
        double centerY = (region.Y + (region.Height / 2)) * ImageHeight;
        OffsetX = (ViewportWidth / 2) - (centerX * Scale);
        OffsetY = (ViewportHeight / 2) - (centerY * Scale);
        ClampOffsets();
    }

    /// <summary>
    /// Gets the visible image region as normalized coordinates, clamped to the full image.
    /// </summary>
    public NormalizedRect GetVisibleNormalizedRect()
    {
        double left = -OffsetX / Scale;
        double top = -OffsetY / Scale;
        double right = (ViewportWidth - OffsetX) / Scale;
        double bottom = (ViewportHeight - OffsetY) / Scale;

        left = Math.Clamp(left, 0, ImageWidth);
        top = Math.Clamp(top, 0, ImageHeight);
        right = Math.Clamp(right, 0, ImageWidth);
        bottom = Math.Clamp(bottom, 0, ImageHeight);

        return right <= left || bottom <= top
            ? NormalizedRect.Empty
            : new NormalizedRect(
                left / ImageWidth,
                top / ImageHeight,
                (right - left) / ImageWidth,
                (bottom - top) / ImageHeight);
    }

    private void ClampOffsets()
    {
        OffsetX = ClampAxis(OffsetX, DisplayWidth, ViewportWidth);
        OffsetY = ClampAxis(OffsetY, DisplayHeight, ViewportHeight);
    }

    private static double ClampAxis(double offset, double displayed, double viewport)
    {
        if (displayed <= viewport)
        {
            return (viewport - displayed) / 2;
        }

        return Math.Clamp(offset, viewport - displayed, 0);
    }
}
