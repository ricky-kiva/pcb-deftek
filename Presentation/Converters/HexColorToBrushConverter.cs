using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace pcb_deftek.Presentation.Converters;

/// <summary>
/// Converts an opaque <c>#RRGGBB</c> defect color into a frozen <see cref="SolidColorBrush"/>
/// suitable for cross-thread WPF binding.
/// </summary>
public sealed class HexColorToBrushConverter : IValueConverter
{
    /// <summary>
    /// Converts a <c>#RRGGBB</c> string to a frozen brush.
    /// Invalid or missing input yields <see cref="Brushes.Transparent"/>.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string text &&
            text.Length == 7 &&
            text[0] == '#' &&
            uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            var brush = new SolidColorBrush(Color.FromRgb(
                (byte)(rgb >> 16),
                (byte)(rgb >> 8),
                (byte)rgb));
            brush.Freeze();
            return brush;
        }

        return Brushes.Transparent;
    }

    /// <summary>
    /// Brush-to-hex conversion is not supported.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
