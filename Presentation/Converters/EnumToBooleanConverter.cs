using System;
using System.Globalization;
using System.Windows.Data;

namespace pcb_deftek.Presentation.Converters;

/// <summary>
/// Converts an enum value to <see langword="true"/> when its name matches the converter parameter.
/// Intended for one-way bindings that drive segmented filter buttons.
/// </summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not null
            && parameter is not null
            && string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
