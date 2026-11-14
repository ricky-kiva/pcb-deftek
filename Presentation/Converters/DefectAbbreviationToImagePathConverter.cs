using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace pcb_deftek.Presentation.Converters;

/// <summary>
/// Resolves a defect abbreviation to its packaged knowledge asset.
/// </summary>
public sealed class DefectAbbreviationToImagePathConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string abbreviation || string.IsNullOrWhiteSpace(abbreviation))
        {
            return null;
        }

        string fileStem = abbreviation.Trim().ToLowerInvariant();
        if (fileStem.Any(character => !char.IsLetterOrDigit(character)))
        {
            return null;
        }

        return new Uri(
            $"/pcb-deftek;component/Assets/Images/dashb-info-{fileStem}.png",
            UriKind.Relative);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
