using System;
using System.Globalization;
using System.Windows;
using FluentAssertions;
using pcb_deftek.Domain.Models;
using pcb_deftek.Presentation.Converters;
using Xunit;

namespace pcb_deftek.Tests;

public class VisibilityConvertersTests
{
    [Theory]
    [InlineData(true, Visibility.Collapsed)]
    [InlineData(false, Visibility.Visible)]
    [InlineData(null, Visibility.Visible)]
    public void InverseBooleanToVisibilityConverter_ConvertsExpectedly(object? input, Visibility expected)
    {
        var converter = new InverseBooleanToVisibilityConverter();
        var result = converter.Convert(input, typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(5, false, Visibility.Visible)]
    [InlineData(0, false, Visibility.Collapsed)]
    [InlineData(5, true, Visibility.Collapsed)]
    [InlineData(0, true, Visibility.Visible)]
    public void CountToVisibilityConverter_ConvertsExpectedly(int count, bool invert, Visibility expected)
    {
        var converter = new CountToVisibilityConverter { Invert = invert };
        var result = converter.Convert(count, typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(BatchItemStatus.Pass)]
    [InlineData(BatchItemStatus.Fail)]
    [InlineData(BatchItemStatus.Error)]
    [InlineData(BatchItemStatus.Processing)]
    [InlineData(BatchItemStatus.Pending)]
    public void BatchStatusConverters_ProduceNonNullBrushes(BatchItemStatus status)
    {
        var fgConverter = new BatchStatusToForegroundBrushConverter();
        var bgConverter = new BatchStatusToBackgroundBrushConverter();

        var fg = fgConverter.Convert(status, typeof(object), null, CultureInfo.InvariantCulture);
        var bg = bgConverter.Convert(status, typeof(object), null, CultureInfo.InvariantCulture);

        fg.Should().NotBeNull();
        bg.Should().NotBeNull();
    }

    [Theory]
    [InlineData("Some text", Visibility.Visible)]
    [InlineData("", Visibility.Collapsed)]
    [InlineData("   ", Visibility.Collapsed)]
    [InlineData(null, Visibility.Collapsed)]
    public void NullOrEmptyToVisibilityConverter_ConvertsExpectedly(string? input, Visibility expected)
    {
        var converter = new NullOrEmptyToVisibilityConverter();
        var result = converter.Convert(input, typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.Should().Be(expected);
    }
}
