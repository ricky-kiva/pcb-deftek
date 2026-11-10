using System;
using System.Collections.Generic;
using FluentAssertions;
using pcb_deftek.Domain.Models;
using Xunit;

namespace pcb_deftek.Tests;

public class DefectKnowledgeBaseTests
{
    [Fact]
    public void DefectClassEnum_ShouldHaveExactlyNineStandardizedValuesWithSequentialZeroBasedIndices()
    {
        // Arrange & Act
        var enumValues = Enum.GetValues<DefectClass>();

        // Assert
        enumValues.Should().HaveCount(9);
        ((int)DefectClass.Short).Should().Be(0);
        ((int)DefectClass.Spur).Should().Be(1);
        ((int)DefectClass.SpuriousCopper).Should().Be(2);
        ((int)DefectClass.Open).Should().Be(3);
        ((int)DefectClass.MouseBite).Should().Be(4);
        ((int)DefectClass.HoleBreakout).Should().Be(5);
        ((int)DefectClass.ConductorScratch).Should().Be(6);
        ((int)DefectClass.ConductorForeignObject).Should().Be(7);
        ((int)DefectClass.BaseMaterialForeignObject).Should().Be(8);
    }

    [Fact]
    public void All_ShouldContainDefinitionsForEveryRegisteredDefectClass()
    {
        // Act
        var all = DefectKnowledgeBase.All;

        // Assert
        all.Should().HaveCount(9);
        foreach (var defectClass in Enum.GetValues<DefectClass>())
        {
            all.Should().ContainKey(defectClass);
            all[defectClass].Class.Should().Be(defectClass);
        }
    }

    [Theory]
    [InlineData(0, "short", "Short", DefectSeverity.Critical)]
    [InlineData(1, "spur", "Spur", DefectSeverity.Medium)]
    [InlineData(2, "spurious_copper", "Spurious copper", DefectSeverity.MediumHigh)]
    [InlineData(3, "open", "Open", DefectSeverity.High)]
    [InlineData(4, "mouse_bite", "Mouse bite", DefectSeverity.LowMedium)]
    [InlineData(5, "hole_breakout", "Hole breakout", DefectSeverity.Medium)]
    [InlineData(6, "conductor_scratch", "Conductor scratch", DefectSeverity.Medium)]
    [InlineData(7, "conductor_foreign_object", "Conductor foreign object", DefectSeverity.Medium)]
    [InlineData(8, "base_material_foreign_object", "Base material foreign object", DefectSeverity.LowMedium)]
    public void GetByIndex_WithValidIndex_ReturnsMatchingDefectClassInfo(
        int index,
        string expectedCode,
        string expectedDisplayName,
        DefectSeverity expectedSeverity)
    {
        // Act
        var info = DefectKnowledgeBase.GetByIndex(index);

        // Assert
        info.ClassId.Should().Be(index);
        info.Code.Should().Be(expectedCode);
        info.DisplayName.Should().Be(expectedDisplayName);
        info.Severity.Should().Be(expectedSeverity);
        info.Explanation.Should().NotBeNullOrWhiteSpace();
        info.RootCause.Should().NotBeNullOrWhiteSpace();
        info.Impact.Should().NotBeNullOrWhiteSpace();
        info.Action.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("short", DefectClass.Short)]
    [InlineData("spur", DefectClass.Spur)]
    [InlineData("spurious_copper", DefectClass.SpuriousCopper)]
    [InlineData("open", DefectClass.Open)]
    [InlineData("mouse_bite", DefectClass.MouseBite)]
    [InlineData("hole_breakout", DefectClass.HoleBreakout)]
    [InlineData("conductor_scratch", DefectClass.ConductorScratch)]
    [InlineData("conductor_foreign_object", DefectClass.ConductorForeignObject)]
    [InlineData("base_material_foreign_object", DefectClass.BaseMaterialForeignObject)]
    public void GetByCode_WithCaseInsensitiveCode_ReturnsMatchingDefectClassInfo(string code, DefectClass expectedClass)
    {
        // Act
        var lowerResult = DefectKnowledgeBase.GetByCode(code.ToLowerInvariant());
        var upperResult = DefectKnowledgeBase.GetByCode(code.ToUpperInvariant());

        // Assert
        lowerResult.Class.Should().Be(expectedClass);
        upperResult.Class.Should().Be(expectedClass);
    }

    [Fact]
    public void GetByIndex_WithNegativeOrOverflowIndex_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => DefectKnowledgeBase.GetByIndex(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DefectKnowledgeBase.GetByIndex(9));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetByCode_WithEmptyOrWhitespace_ThrowsArgumentException(string invalidCode)
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() => DefectKnowledgeBase.GetByCode(invalidCode));
    }

    [Fact]
    public void GetByCode_WithNull_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => DefectKnowledgeBase.GetByCode(null!));
    }

    [Fact]
    public void GetByCode_WithUnknownCode_ThrowsKeyNotFoundException()
    {
        // Act & Assert
        Assert.Throws<KeyNotFoundException>(() => DefectKnowledgeBase.GetByCode("unknown_defect_code"));
    }
}
