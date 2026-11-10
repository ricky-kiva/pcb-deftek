using System;
using pcb_deftek.Common.Exceptions;

namespace pcb_deftek.Domain.Models;

/// <summary>
/// Immutable spatial bounding box with normalized coordinates within [0.0, 1.0].
/// </summary>
public readonly record struct BoundingBox
{
    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }

    public BoundingBox(float x, float y, float width, float height)
    {
        if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(width) || float.IsNaN(height))
            throw new ValidationException("Bounding box coordinates cannot be NaN.", nameof(BoundingBox), null);

        if (width <= 0.0f || height <= 0.0f)
            throw new ValidationException("Bounding box dimensions must be strictly positive.", nameof(BoundingBox), $"{width}x{height}");

        if (x < 0.0f || y < 0.0f || x + width > 1.0001f || y + height > 1.0001f)
            throw new ValidationException("Bounding box coordinates must be normalized within range [0.0, 1.0].", nameof(BoundingBox), $"({x},{y},{width},{height})");

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public static BoundingBox FromNormalized(float x, float y, float width, float height) =>
        new(x, y, width, height);

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

    public static BoundingBox Clamp(float x, float y, float width, float height)
    {
        float clampedX = Math.Clamp(x, 0.0f, 1.0f);
        float clampedY = Math.Clamp(y, 0.0f, 1.0f);
        float maxW = Math.Max(0.0001f, 1.0f - clampedX);
        float maxH = Math.Max(0.0001f, 1.0f - clampedY);
        float clampedW = Math.Clamp(width, 0.0001f, maxW);
        float clampedH = Math.Clamp(height, 0.0001f, maxH);

        return new BoundingBox(clampedX, clampedY, clampedW, clampedH);
    }
}
