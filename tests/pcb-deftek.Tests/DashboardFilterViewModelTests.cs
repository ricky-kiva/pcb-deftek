using System;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Presentation.Models;
using pcb_deftek.Presentation.ViewModels;
using Xunit;

namespace pcb_deftek.Tests;

public class DashboardFilterViewModelTests
{
    private static DateTime LocalStartOfDayUtc(DateTime date)
        => DateTime.SpecifyKind(date.Date, DateTimeKind.Local).ToUniversalTime();

    private static DateTime LocalEndOfDayUtc(DateTime date)
        => DateTime.SpecifyKind(date.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local).ToUniversalTime();

    [Fact]
    public void Defaults_ProduceAllTimeFilterWithAllOptions()
    {
        var sut = new DashboardFilterViewModel();

        sut.SelectedPreset.Should().Be(DateRangePreset.None);
        sut.HasActiveFilters.Should().BeFalse();
        sut.ClassOptions.Should().HaveCount(9);
        sut.SeverityOptions.Should().HaveCount(5);
        sut.DateRangeLabel.Should().Be("All time");

        sut.TryCreateFilter(out var filter).Should().BeTrue();
        filter!.StartDateUtc.Should().BeNull();
        filter.EndDateUtc.Should().BeNull();
        filter.DefectClasses.Should().BeEmpty();
        filter.Severities.Should().BeEmpty();
    }

    [Fact]
    public void ClassAndSeverityOptions_AreOrderedAndLabelled()
    {
        var sut = new DashboardFilterViewModel();

        sut.ClassOptions.Select(option => option.Value).Should()
            .Equal(DefectKnowledgeBase.GetAll().OrderBy(info => info.ClassId).Select(info => info.Class));
        sut.SeverityOptions.Select(option => option.Value).Should()
            .Equal(Enum.GetValues<DefectSeverity>().OrderByDescending(value => value));
        sut.ClassOptions.Should().OnlyContain(option => option.Label.Contains('(') && option.Label.Contains(')'));
        sut.SeverityOptions.Select(option => option.Label).Should()
            .Contain(new[] { "Critical", "High", "Medium-high", "Medium", "Low-medium" });
    }

    [Fact]
    public void DayPreset_ProducesInclusiveLocalBoundsInUtc()
    {
        var sut = new DashboardFilterViewModel { SelectedPreset = DateRangePreset.Day };

        sut.TryCreateFilter(out var filter).Should().BeTrue();

        filter!.StartDateUtc.Should().Be(LocalStartOfDayUtc(DateTime.Today));
        filter.EndDateUtc.Should().Be(LocalEndOfDayUtc(DateTime.Today));
        sut.HasActiveFilters.Should().BeTrue();
        sut.DateRangeLabel.Should().Be("Today");
    }

    [Fact]
    public void WeekPreset_UsesCultureFirstDayOfWeek()
    {
        var sut = new DashboardFilterViewModel { SelectedPreset = DateRangePreset.Week };

        sut.TryCreateFilter(out var filter).Should().BeTrue();

        var today = DateTime.Today;
        int firstDay = (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        int offset = ((int)today.DayOfWeek - firstDay + 7) % 7;
        var weekStart = today.AddDays(-offset);

        filter!.StartDateUtc.Should().Be(LocalStartOfDayUtc(weekStart));
        filter.EndDateUtc.Should().Be(LocalEndOfDayUtc(weekStart.AddDays(6)));
    }

    [Fact]
    public void CustomRange_WithValidDates_ProducesInclusiveBounds()
    {
        var start = new DateTime(2026, 11, 1);
        var end = new DateTime(2026, 11, 5);
        var sut = new DashboardFilterViewModel
        {
            SelectedPreset = DateRangePreset.Custom,
            CustomStartDate = start,
            CustomEndDate = end
        };

        sut.TryCreateFilter(out var filter).Should().BeTrue();

        filter!.StartDateUtc.Should().Be(LocalStartOfDayUtc(start));
        filter.EndDateUtc.Should().Be(LocalEndOfDayUtc(end));
        filter.BucketGranularityOverride.Should().BeNull();
        sut.DateRangeLabel.Should().Contain("1");
        sut.DateRangeLabel.Should().Contain("5");
    }

    [Fact]
    public void CustomRange_WithFullYear_RequestsMonthlyBuckets()
    {
        var sut = new DashboardFilterViewModel
        {
            SelectedPreset = DateRangePreset.Custom,
            CustomStartDate = new DateTime(2026, 1, 1),
            CustomEndDate = new DateTime(2026, 12, 31)
        };

        sut.TryCreateFilter(out var filter).Should().BeTrue();

        filter!.BucketGranularityOverride.Should().Be(DashboardBucketGranularity.Month);
    }

    [Fact]
    public void CustomRange_WithStartAfterEnd_IsInvalidAndProducesNoFilter()
    {
        var sut = new DashboardFilterViewModel
        {
            SelectedPreset = DateRangePreset.Custom,
            CustomStartDate = new DateTime(2026, 11, 5),
            CustomEndDate = new DateTime(2026, 11, 1)
        };

        sut.IsCustomRangeValid.Should().BeFalse();
        sut.CustomRangeError.Should().NotBeNullOrWhiteSpace();
        sut.TryCreateFilter(out var filter).Should().BeFalse();
        filter.Should().BeNull();
    }

    [Fact]
    public void CustomRange_WithMissingDate_ProducesNoFilter()
    {
        var sut = new DashboardFilterViewModel
        {
            SelectedPreset = DateRangePreset.Custom,
            CustomStartDate = new DateTime(2026, 11, 1)
        };

        sut.TryCreateFilter(out var filter).Should().BeFalse();
        filter.Should().BeNull();
    }

    [Fact]
    public void SelectedClassesAndSeverities_ProjectAndReportLabels()
    {
        var sut = new DashboardFilterViewModel();
        sut.ClassOptions.Single(option => option.Value == DefectClass.Short).IsSelected = true;
        sut.ClassOptions.Single(option => option.Value == DefectClass.Open).IsSelected = true;
        sut.SeverityOptions.Single(option => option.Value == DefectSeverity.Critical).IsSelected = true;

        sut.TryCreateFilter(out var filter).Should().BeTrue();

        filter!.DefectClasses.Should().BeEquivalentTo(new[] { DefectClass.Short, DefectClass.Open });
        filter.Severities.Should().BeEquivalentTo(new[] { DefectSeverity.Critical });
        sut.ClassFilterLabel.Should().Be("2 classes");
        sut.SeverityFilterLabel.Should().Be("Critical");
        sut.HasActiveFilters.Should().BeTrue();
    }

    [Fact]
    public void AllNineClasses_WithoutSeverity_BehavesLikeNoDefectFilter()
    {
        var sut = new DashboardFilterViewModel();
        foreach (var option in sut.ClassOptions)
        {
            option.IsSelected = true;
        }

        sut.TryCreateFilter(out var filter).Should().BeTrue();

        filter!.DefectClasses.Should().HaveCount(9);
        filter.HasDefectFilters.Should().BeFalse();
    }

    [Fact]
    public void AllNineClasses_AreNotReportedAsActiveFilter()
    {
        var sut = new DashboardFilterViewModel();
        foreach (var option in sut.ClassOptions)
        {
            option.IsSelected = true;
        }

        sut.HasActiveFilters.Should().BeFalse();
    }

    [Fact]
    public void FilterChanges_RaiseSingleEventPerUserChange()
    {
        var sut = new DashboardFilterViewModel();
        int changes = 0;
        sut.FiltersChanged += (_, _) => changes++;

        sut.SelectedPreset = DateRangePreset.Day;
        sut.ClassOptions[0].IsSelected = true;
        sut.SeverityOptions[0].IsSelected = true;

        changes.Should().Be(3);
    }

    [Fact]
    public void ClearAll_ResetsEverythingAndRaisesOnce()
    {
        var sut = new DashboardFilterViewModel { SelectedPreset = DateRangePreset.Day };
        sut.ClassOptions[0].IsSelected = true;
        sut.SeverityOptions[0].IsSelected = true;
        sut.CustomStartDate = DateTime.Today;
        sut.CustomEndDate = DateTime.Today;
        int changes = 0;
        sut.FiltersChanged += (_, _) => changes++;

        sut.ClearAll();

        changes.Should().Be(1);
        sut.SelectedPreset.Should().Be(DateRangePreset.None);
        sut.CustomStartDate.Should().BeNull();
        sut.CustomEndDate.Should().BeNull();
        sut.ClassOptions.Should().OnlyContain(option => !option.IsSelected);
        sut.SeverityOptions.Should().OnlyContain(option => !option.IsSelected);
        sut.HasActiveFilters.Should().BeFalse();
        sut.TryCreateFilter(out var filter).Should().BeTrue();
        filter!.StartDateUtc.Should().BeNull();
        filter.EndDateUtc.Should().BeNull();
    }
}
