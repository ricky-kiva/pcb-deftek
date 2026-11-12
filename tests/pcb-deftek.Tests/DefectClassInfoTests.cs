using System;
using System.Linq;
using FluentAssertions;
using pcb_deftek.Domain.Models;
using Xunit;

namespace pcb_deftek.Tests;

public class DefectClassInfoTests
{
    [Fact]
    public void AllNineDefectClasses_HaveCompleteValidMetadata()
    {
        // Arrange
        var classes = Enum.GetValues<DefectClass>();

        // Assert
        classes.Should().HaveCount(9);

        foreach (var defectClass in classes)
        {
            var info = DefectKnowledgeBase.GetInfo(defectClass);

            info.Should().NotBeNull();
            info.Class.Should().Be(defectClass);
            info.ClassId.Should().BeInRange(0, 8);
            info.Code.Should().NotBeNullOrWhiteSpace();
            info.Abbreviation.Should().NotBeNullOrWhiteSpace().And.MatchRegex("^[A-Z]{2,4}$");
            info.DisplayName.Should().NotBeNullOrWhiteSpace();
            info.Explanation.Should().NotBeNullOrWhiteSpace();
            info.RootCause.Should().NotBeNullOrWhiteSpace();
            info.Impact.Should().NotBeNullOrWhiteSpace();
            info.Action.Should().NotBeNullOrWhiteSpace();
            Enum.IsDefined(typeof(DefectSeverity), info.Severity).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData(DefectClass.Short, "SH")]
    [InlineData(DefectClass.Open, "OP")]
    [InlineData(DefectClass.MouseBite, "MB")]
    [InlineData(DefectClass.Spur, "SP")]
    [InlineData(DefectClass.SpuriousCopper, "SC")]
    [InlineData(DefectClass.HoleBreakout, "HB")]
    [InlineData(DefectClass.ConductorScratch, "CS")]
    [InlineData(DefectClass.ConductorForeignObject, "CFO")]
    [InlineData(DefectClass.BaseMaterialForeignObject, "BMFO")]
    public void DefectClassInfo_Abbreviations_StrictlyMatchConstitutionTable(DefectClass defectClass, string expectedAbbreviation)
    {
        var info = DefectKnowledgeBase.GetInfo(defectClass);
        info.Abbreviation.Should().Be(expectedAbbreviation);
    }

    [Fact]
    public void DefectClassInfo_RecordEquality_BehavesAsValueObject()
    {
        var info1 = DefectKnowledgeBase.GetInfo(DefectClass.Short);
        var info2 = DefectKnowledgeBase.GetByAbbreviation("SH");

        (info1 == info2).Should().BeTrue();
        info1.Equals(info2).Should().BeTrue();
        info1.GetHashCode().Should().Be(info2.GetHashCode());
    }

    [Fact]
    public void DefectClassInfo_AllAbbreviations_AreUnique()
    {
        var all = DefectKnowledgeBase.GetAll();
        var abbreviations = all.Select(d => d.Abbreviation).ToList();

        abbreviations.Should().OnlyHaveUniqueItems();
        abbreviations.Should().HaveCount(9);
    }
}
