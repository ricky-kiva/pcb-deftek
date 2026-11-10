using System;

namespace pcb_deftek.Infrastructure.Vision;

/// <summary>
/// Captures the geometric scaling and padding parameters used to letterbox an image into model input dimensions.
/// Provides methods to invert model detections back to original pixel and normalized coordinate spaces.
/// </summary>
public record LetterboxTransform(
    int OriginalWidth,
    int OriginalHeight,
    int TargetWidth,
    int TargetHeight,
    float Scale,
    float PadX,
    float PadY)
{
    /// <summary>
    /// Inverts a point from the target letterbox coordinate space back into original image pixel coordinates.
    /// </summary>
    /// <param name="x">The X coordinate in letterboxed model space.</param>
    /// <param name="y">The Y coordinate in letterboxed model space.</param>
    /// <returns>The unpadded, unscaled pixel coordinates clamped to original image boundaries.</returns>
    public (float X, float Y) InvertPoint(float x, float y)
    {
        float originalX = (x - PadX) / Scale;
        float originalY = (y - PadY) / Scale;

        return (
            Math.Clamp(originalX, 0f, OriginalWidth),
            Math.Clamp(originalY, 0f, OriginalHeight));
    }

    /// <summary>
    /// Inverts a bounding box from letterbox coordinates (in pixels 0..TargetWidth/Height)
    /// to normalized [0.0, 1.0] coordinates relative to the original image dimensions.
    /// </summary>
    /// <param name="boxX">Top-left X in letterbox space.</param>
    /// <param name="boxY">Top-left Y in letterbox space.</param>
    /// <param name="boxWidth">Width in letterbox space.</param>
    /// <param name="boxHeight">Height in letterbox space.</param>
    /// <returns>Normalized X, Y, Width, and Height clamped to [0.0, 1.0].</returns>
    public (float NormX, float NormY, float NormWidth, float NormHeight) InvertBoxToNormalized(
        float boxX,
        float boxY,
        float boxWidth,
        float boxHeight)
    {
        float origX1 = (boxX - PadX) / Scale;
        float origY1 = (boxY - PadY) / Scale;
        float origX2 = (boxX + boxWidth - PadX) / Scale;
        float origY2 = (boxY + boxHeight - PadY) / Scale;

        float clampedX1 = Math.Clamp(origX1, 0f, OriginalWidth);
        float clampedY1 = Math.Clamp(origY1, 0f, OriginalHeight);
        float clampedX2 = Math.Clamp(origX2, 0f, OriginalWidth);
        float clampedY2 = Math.Clamp(origY2, 0f, OriginalHeight);

        float normX = clampedX1 / OriginalWidth;
        float normY = clampedY1 / OriginalHeight;
        float normW = Math.Max(0.0001f, (clampedX2 - clampedX1) / OriginalWidth);
        float normH = Math.Max(0.0001f, (clampedY2 - clampedY1) / OriginalHeight);

        return (
            Math.Clamp(normX, 0f, 1f),
            Math.Clamp(normY, 0f, 1f),
            Math.Clamp(normW, 0.0001f, 1f - normX),
            Math.Clamp(normH, 0.0001f, 1f - normY));
    }
}
