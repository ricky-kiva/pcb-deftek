using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using FluentAssertions;
using pcb_deftek.Domain.Models;
using pcb_deftek.Presentation.Converters;
using Xunit;

namespace pcb_deftek.Tests;

public class DefectPaletteTests
{
    [Theory]
    [InlineData(DefectClass.Short, "#0066FF")]
    [InlineData(DefectClass.Spur, "#00DCDC")]
    [InlineData(DefectClass.SpuriousCopper, "#FFFFFF")]
    [InlineData(DefectClass.Open, "#4FA050")]
    [InlineData(DefectClass.MouseBite, "#00308C")]
    [InlineData(DefectClass.HoleBreakout, "#FF69B4")]
    [InlineData(DefectClass.ConductorScratch, "#E81123")]
    [InlineData(DefectClass.ConductorForeignObject, "#FFD700")]
    [InlineData(DefectClass.BaseMaterialForeignObject, "#2ECC71")]
    public void ColorHex_ReturnsEstablishedPalette(DefectClass defectClass, string expectedHex)
    {
        DefectKnowledgeBase.GetInfo(defectClass).ColorHex.Should().Be(expectedHex);
    }

    [Fact]
    public void ColorHex_AllNineClassesAreValidAndDistinct()
    {
        var colors = DefectKnowledgeBase.GetAll().Select(info => info.ColorHex).ToList();

        colors.Should().HaveCount(9);
        colors.Should().OnlyContain(color =>
            color.Length == 7 &&
            color[0] == '#' &&
            color.Substring(1).All(Uri.IsHexDigit));
        colors.Distinct(StringComparer.OrdinalIgnoreCase).Should().HaveCount(9);
    }

    [Fact]
    public void Converter_ConvertsHexToFrozenBrush()
    {
        var converter = new HexColorToBrushConverter();

        var brush = converter.Convert(
            "#E81123",
            typeof(SolidColorBrush),
            null,
            CultureInfo.InvariantCulture) as SolidColorBrush;

        brush.Should().NotBeNull();
        brush!.Color.Should().Be(Color.FromRgb(0xE8, 0x11, 0x23));
        brush.IsFrozen.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-color")]
    public void Converter_WithInvalidInput_ReturnsTransparentBrush(object? value)
    {
        var converter = new HexColorToBrushConverter();

        var brush = converter.Convert(value, typeof(SolidColorBrush), null, CultureInfo.InvariantCulture);

        brush.Should().BeSameAs(Brushes.Transparent);
    }
}
