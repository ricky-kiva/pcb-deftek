using System;
using pcb_deftek.Common.Exceptions;

namespace pcb_deftek.Domain.Models;

/// <summary>
/// Immutable spatial bounding box with normalized coordinates within [0.0, 1.0].
/// Designed to be difficult to misuse: coordinates and dimensions are strictly validated on construction.
/// </summary>
public readonly record struct BoundingBox
{
    private const float CoordinateEpsilon = 0.0001f;

    /// <summary>
    /// Gets the normalized X coordinate of the top-left origin (0.0 to 1.0).
    /// </summary>
    public float X { get; }

    /// <summary>
    /// Gets the normalized Y coordinate of the top-left origin (0.0 to 1.0).
    /// </summary>
    public float Y { get; }

    /// <summary>
    /// Gets the normalized width (0.0 to 1.0).
    /// </summary>
    public float Width { get; }

    /// <summary>
    /// Gets the normalized height (0.0 to 1.0).
    /// </summary>
    public float Height { get; }

    /// <summary>
    /// Gets the normalized right boundary coordinate (X + Width).
    /// </summary>
    public float Right => X + Width;

    /// <summary>
    /// Gets the normalized bottom boundary coordinate (Y + Height).
    /// </summary>
    public float Bottom => Y + Height;

    /// <summary>
    /// Gets the normalized horizontal center coordinate.
    /// </summary>
    public float CenterX => X + (Width / 2.0f);

    /// <summary>
    /// Gets the normalized vertical center coordinate.
    /// </summary>
    public float CenterY => Y + (Height / 2.0f);

    /// <summary>
    /// Gets the normalized area covered by the bounding box.
    /// </summary>
    public float Area => Width * Height;

    public BoundingBox(float x, float y, float width, float height)
    {
        if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(width) || float.IsNaN(height))
            throw new ValidationException("Bounding box coordinates cannot be NaN.", nameof(BoundingBox), null);

        if (float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(width) || float.IsInfinity(height))
            throw new ValidationException("Bounding box coordinates cannot be infinite.", nameof(BoundingBox), null);

        if (width <= 0.0f || height <= 0.0f)
            throw new ValidationException("Bounding box dimensions must be strictly positive.", nameof(BoundingBox), $"{width}x{height}");

        if (x < 0.0f || y < 0.0f || x + width > 1.0f + CoordinateEpsilon || y + height > 1.0f + CoordinateEpsilon)
            throw new ValidationException("Bounding box coordinates must be normalized within range [0.0, 1.0].", nameof(BoundingBox), $"({x},{y},{width},{height})");

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Creates a new bounding box from normalized coordinates.
    /// </summary>
    public static BoundingBox FromNormalized(float x, float y, float width, float height) =>
        new(x, y, width, height);

    /// <summary>
    /// Creates a normalized bounding box by scaling pixel coordinates against total image dimensions.
    /// </summary>
    public static BoundingBox FromPixels(int x, int y, int width, int height, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
            throw new ValidationException("Image dimensions must be strictly positive.", nameof(imageWidth), $"{imageWidth}x{imageHeight}");

        float normX = (float)x / imageWidth;
        float normY = (float)y / imageHeight;
        float normW = (float)width / imageWidth;
        float normH = (float)height / imageHeight;

        return Clamp(normX, normY, normW, normH);
    }

    /// <summary>
    /// Clamps arbitrary coordinates safely into the valid normalized range [0.0, 1.0].
    /// </summary>
    public static BoundingBox Clamp(float x, float y, float width, float height)
    {
        float clampedX = Math.Clamp(x, 0.0f, 1.0f);
        float clampedY = Math.Clamp(y, 0.0f, 1.0f);
        float maxW = Math.Max(CoordinateEpsilon, 1.0f - clampedX);
        float maxH = Math.Max(CoordinateEpsilon, 1.0f - clampedY);
        float clampedW = Math.Clamp(width, CoordinateEpsilon, maxW);
        float clampedH = Math.Clamp(height, CoordinateEpsilon, maxH);

        return new BoundingBox(clampedX, clampedY, clampedW, clampedH);
    }

    /// <summary>
    /// Converts normalized coordinates back to discrete pixel coordinates given target image dimensions.
    /// </summary>
    public void ToPixels(int imageWidth, int imageHeight, out int pixelX, out int pixelY, out int pixelWidth, out int pixelHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
            throw new ValidationException("Image dimensions must be strictly positive.", nameof(imageWidth), $"{imageWidth}x{imageHeight}");

        pixelX = (int)Math.Round(X * imageWidth);
        pixelY = (int)Math.Round(Y * imageHeight);
        pixelWidth = (int)Math.Round(Width * imageWidth);
        pixelHeight = (int)Math.Round(Height * imageHeight);
    }

    /// <summary>
    /// Determines whether the specified normalized point lies within this bounding box.
    /// </summary>
    public bool Contains(float pointX, float pointY) =>
        pointX >= X && pointX <= Right && pointY >= Y && pointY <= Bottom;

    /// <summary>
    /// Deconstructs the bounding box into its component normalized coordinates.
    /// </summary>
    public void Deconstruct(out float x, out float y, out float width, out float height)
    {
        x = X;
        y = Y;
        width = Width;
        height = Height;
    }
}
