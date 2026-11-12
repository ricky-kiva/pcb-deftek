using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Presentation.Converters;

/// <summary>
/// Converts a boolean value to an inverted <see cref="Visibility"/> (false -> Visible, true -> Collapsed).
/// </summary>
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? Visibility.Collapsed : Visibility.Visible;
        }

        return Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Visibility v)
        {
            return v != Visibility.Visible;
        }

        return false;
    }
}

/// <summary>
/// Converts a string value to <see cref="Visibility"/> (non-null and non-whitespace -> Visible, otherwise Collapsed).
/// </summary>
public class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isVisible = value switch
        {
            string s => !string.IsNullOrWhiteSpace(s),
            _ => value != null
        };

        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts an integer collection count to <see cref="Visibility"/> (count > 0 -> Visible, count == 0 -> Collapsed).
/// </summary>
public class CountToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int count = value switch
        {
            int i => i,
            long l => (int)l,
            _ => 0
        };

        bool hasItems = count > 0;
        if (Invert)
        {
            hasItems = !hasItems;
        }

        return hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts a <see cref="BatchItemStatus"/> enum value to a foreground <see cref="SolidColorBrush"/>.
/// </summary>
public class BatchStatusToForegroundBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush WhiteTextBrush = new(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush PendingBrush = new(Color.FromRgb(0x8A, 0x8A, 0x8A));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is BatchItemStatus status)
        {
            return status switch
            {
                BatchItemStatus.Pass => WhiteTextBrush,
                BatchItemStatus.Fail => WhiteTextBrush,
                BatchItemStatus.Error => WhiteTextBrush,
                BatchItemStatus.Processing => WhiteTextBrush,
                _ => PendingBrush
            };
        }

        return PendingBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts a <see cref="BatchItemStatus"/> enum value to a light tint background <see cref="SolidColorBrush"/> for status badges.
/// </summary>
public class BatchStatusToBackgroundBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush PassBgBrush = new(Color.FromRgb(0x10, 0x7C, 0x41));      // Solid Forest Green
    private static readonly SolidColorBrush FailBgBrush = new(Color.FromRgb(0xC4, 0x2B, 0x1C));      // Solid Crimson Red (High Contrast, WCAG AA compliant)
    private static readonly SolidColorBrush ErrorBgBrush = new(Color.FromRgb(0xD8, 0x3B, 0x01));     // Solid Orange-Red
    private static readonly SolidColorBrush ProcessingBgBrush = new(Color.FromRgb(0x00, 0x78, 0xD4)); // Solid Fluent Blue
    private static readonly SolidColorBrush PendingBgBrush = new(Color.FromArgb(0x2E, 0x73, 0x73, 0x73));    // Subtle Neutral Tint

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is BatchItemStatus status)
        {
            return status switch
            {
                BatchItemStatus.Pass => PassBgBrush,
                BatchItemStatus.Fail => FailBgBrush,
                BatchItemStatus.Error => ErrorBgBrush,
                BatchItemStatus.Processing => ProcessingBgBrush,
                _ => PendingBgBrush
            };
        }

        return PendingBgBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
