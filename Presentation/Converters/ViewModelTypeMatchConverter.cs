using System;
using System.Globalization;
using System.Windows.Data;

namespace pcb_deftek.Presentation.Converters;

/// <summary>
/// Value converter returning true if the bound object's runtime type matches <see cref="TargetType"/>.
/// Used for two-way or one-way active route highlighting in navigation menus.
/// </summary>
public class ViewModelTypeMatchConverter : IValueConverter
{
    /// <summary>
    /// Gets or sets the target ViewModel type to match against.
    /// </summary>
    public Type? TargetType { get; set; }

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || TargetType == null)
        {
            return false;
        }

        return value.GetType() == TargetType;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
