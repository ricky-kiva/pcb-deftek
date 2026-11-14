using System;

namespace pcb_deftek.Domain.Models;

/// <summary>
/// Immutable normalized rectangle expressed as fractions of an image's width and height.
/// A zero-sized rectangle represents an empty region.
/// </summary>
public readonly record struct NormalizedRect
{
    private const double Epsilon = 0.000001;

    /// <summary>Gets the normalized left coordinate.</summary>
    public double X { get; }

    /// <summary>Gets the normalized top coordinate.</summary>
    public double Y { get; }

    /// <summary>Gets the normalized width.</summary>
    public double Width { get; }

    /// <summary>Gets the normalized height.</summary>
    public double Height { get; }

    /// <summary>Gets the normalized right coordinate.</summary>
    public double Right => X + Width;

    /// <summary>Gets the normalized bottom coordinate.</summary>
    public double Bottom => Y + Height;

    /// <summary>Gets a value indicating whether this rectangle has no area.</summary>
    public bool IsEmpty => Width <= Epsilon || Height <= Epsilon;

    /// <summary>Gets the full-image rectangle.</summary>
    public static NormalizedRect Full => new(0, 0, 1, 1);

    /// <summary>Gets an empty rectangle.</summary>
    public static NormalizedRect Empty => default;

    public NormalizedRect(double x, double y, double width, double height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Normalized rectangle values must be finite.");
        }

        if (x < 0 || y < 0 || width < 0 || height < 0 || x + width > 1 + Epsilon || y + height > 1 + Epsilon)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Normalized rectangle must fit within [0, 1].");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Clamps arbitrary edges into the unit square. Returns <see cref="Empty"/> when the
    /// clamped rectangle has no area.
    /// </summary>
    public static NormalizedRect Clamp(double x, double y, double width, double height)
    {
        double left = Math.Clamp(x, 0, 1);
        double top = Math.Clamp(y, 0, 1);
        double right = Math.Clamp(x + width, 0, 1);
        double bottom = Math.Clamp(y + height, 0, 1);

        return right <= left || bottom <= top
            ? Empty
            : new NormalizedRect(left, top, right - left, bottom - top);
    }
}
