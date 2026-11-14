using System;
using System.Globalization;
using System.Windows.Data;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Presentation.Converters;

/// <summary>
/// Converts the persisted dashboard layout to a UniformGrid column count.
/// </summary>
public sealed class DashboardColumnLayoutToColumnsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DashboardColumnLayout.OneColumn ? 1 : 2;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
