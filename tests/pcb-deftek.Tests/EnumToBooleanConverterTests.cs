using System;
using System.Globalization;
using FluentAssertions;
using pcb_deftek.Presentation.Converters;
using pcb_deftek.Presentation.ViewModels;
using pcb_deftek.Presentation.Models;
using Xunit;

namespace pcb_deftek.Tests;

public class EnumToBooleanConverterTests
{
    private readonly EnumToBooleanConverter _converter = new();

    [Theory]
    [InlineData(DateRangePreset.Day, "Day", true)]
    [InlineData(DateRangePreset.Day, "day", true)]
    [InlineData(DateRangePreset.Day, "Week", false)]
    [InlineData(DateRangePreset.None, "Day", false)]
    public void Convert_MatchesEnumName(DateRangePreset value, string parameter, bool expected)
    {
        var result = _converter.Convert(value, typeof(bool), parameter, CultureInfo.InvariantCulture);

        result.Should().Be(expected);
    }

    [Fact]
    public void Convert_WithNullValue_ReturnsFalse()
    {
        var result = _converter.Convert(null, typeof(bool), "Day", CultureInfo.InvariantCulture);

        result.Should().Be(false);
    }
}
