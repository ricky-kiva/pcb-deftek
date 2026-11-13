using System;
using System.Globalization;
using FluentAssertions;
using pcb_deftek.Presentation.Converters;
using pcb_deftek.Presentation.ViewModels;
using Xunit;

namespace pcb_deftek.Tests;

public class EnumToBooleanConverterTests
{
    private readonly EnumToBooleanConverter _converter = new();

    [Theory]
    [InlineData(HistoryDatePreset.Day, "Day", true)]
    [InlineData(HistoryDatePreset.Day, "day", true)]
    [InlineData(HistoryDatePreset.Day, "Week", false)]
    [InlineData(HistoryDatePreset.None, "Day", false)]
    public void Convert_MatchesEnumName(HistoryDatePreset value, string parameter, bool expected)
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
