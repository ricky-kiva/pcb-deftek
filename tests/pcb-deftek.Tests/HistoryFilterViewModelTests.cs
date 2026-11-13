using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using pcb_deftek.Domain.Models;
using pcb_deftek.Presentation.ViewModels;
using Xunit;

namespace pcb_deftek.Tests;

public class HistoryFilterViewModelTests
{
    private static DateTime LocalStartOfDayUtc(DateTime date)
        => DateTime.SpecifyKind(date.Date, DateTimeKind.Local).ToUniversalTime();

    private static DateTime LocalEndOfDayUtc(DateTime date)
        => DateTime.SpecifyKind(date.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local).ToUniversalTime();

    [Fact]
    public void Defaults_ApplyNoFilters()
    {
        var sut = new HistoryFilterViewModel();

        sut.SelectedPreset.Should().Be(HistoryDatePreset.None);
        sut.HasActiveFilters.Should().BeFalse();
        sut.SelectedStatuses.Should().BeEmpty();
        sut.SelectedClasses.Should().BeEmpty();
        sut.IsCustomRangeValid.Should().BeTrue();

        var filter = sut.ToQueryFilter(pageNumber: 1, pageSize: 60);
        filter.StartDateUtc.Should().BeNull();
        filter.EndDateUtc.Should().BeNull();
        filter.Statuses.Should().BeEmpty();
        filter.DefectClasses.Should().BeEmpty();
        filter.PageNumber.Should().Be(1);
        filter.PageSize.Should().Be(60);
    }

    [Fact]
    public void StatusAndClassOptions_AreSelectableAndProjectedIntoQueryFilter()
    {
        var sut = new HistoryFilterViewModel();

        sut.StatusOptions.Single(option => option.Value == InspectionStatus.Fail).IsSelected = true;
        sut.ClassOptions.Single(option => option.Value == DefectClass.Short).IsSelected = true;
        sut.ClassOptions.Single(option => option.Value == DefectClass.Open).IsSelected = true;

        sut.HasActiveFilters.Should().BeTrue();
        sut.SelectedStatuses.Should().BeEquivalentTo(new[] { InspectionStatus.Fail });
        sut.SelectedClasses.Should().BeEquivalentTo(new[] { DefectClass.Short, DefectClass.Open });

        var filter = sut.ToQueryFilter(pageNumber: 2, pageSize: 25);
        filter.Statuses.Should().BeEquivalentTo(new[] { InspectionStatus.Fail });
        filter.DefectClasses.Should().BeEquivalentTo(new[] { DefectClass.Short, DefectClass.Open });
        filter.PageNumber.Should().Be(2);
        filter.PageSize.Should().Be(25);
    }

    [Fact]
    public void ClassOptions_ContainAllNineClassesWithAbbreviations()
    {
        var sut = new HistoryFilterViewModel();

        sut.ClassOptions.Should().HaveCount(9);
        sut.ClassOptions.Select(option => option.Value).Should().BeEquivalentTo(Enum.GetValues<DefectClass>());
        sut.ClassOptions.Should().OnlyContain(option => option.Label.Contains('(') && option.Label.Contains(')'));
    }

    [Fact]
    public void DayPreset_ProducesInclusiveLocalDayBoundsInUtc()
    {
        var sut = new HistoryFilterViewModel { SelectedPreset = HistoryDatePreset.Day };

        var filter = sut.ToQueryFilter(1, 60);

        filter.StartDateUtc.Should().Be(LocalStartOfDayUtc(DateTime.Today));
        filter.EndDateUtc.Should().Be(LocalEndOfDayUtc(DateTime.Today));
    }

    [Fact]
    public void WeekPreset_UsesCultureFirstDayOfWeek()
    {
        var sut = new HistoryFilterViewModel { SelectedPreset = HistoryDatePreset.Week };

        var filter = sut.ToQueryFilter(1, 60);

        var today = DateTime.Today;
        int firstDay = (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        int offset = ((int)today.DayOfWeek - firstDay + 7) % 7;
        var weekStart = today.AddDays(-offset);

        filter.StartDateUtc.Should().Be(LocalStartOfDayUtc(weekStart));
        filter.EndDateUtc.Should().Be(LocalEndOfDayUtc(weekStart.AddDays(6)));
    }

    [Fact]
    public void MonthPreset_ProducesCurrentMonthBounds()
    {
        var sut = new HistoryFilterViewModel { SelectedPreset = HistoryDatePreset.Month };

        var filter = sut.ToQueryFilter(1, 60);

        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        filter.StartDateUtc.Should().Be(LocalStartOfDayUtc(monthStart));
        filter.EndDateUtc.Should().Be(LocalEndOfDayUtc(monthEnd));
    }

    [Fact]
    public void CustomPreset_WithValidRange_ProducesInclusiveBounds()
    {
        var start = new DateTime(2026, 11, 1);
        var end = new DateTime(2026, 11, 5);
        var sut = new HistoryFilterViewModel
        {
            SelectedPreset = HistoryDatePreset.Custom,
            CustomStartDate = start,
            CustomEndDate = end
        };

        sut.IsCustomRangeValid.Should().BeTrue();

        var filter = sut.ToQueryFilter(1, 60);
        filter.StartDateUtc.Should().Be(LocalStartOfDayUtc(start));
        filter.EndDateUtc.Should().Be(LocalEndOfDayUtc(end));
    }

    [Fact]
    public void CustomPreset_WithStartAfterEnd_IsInvalidAndOmitsBounds()
    {
        var sut = new HistoryFilterViewModel
        {
            SelectedPreset = HistoryDatePreset.Custom,
            CustomStartDate = new DateTime(2026, 11, 5),
            CustomEndDate = new DateTime(2026, 11, 1)
        };

        sut.IsCustomRangeValid.Should().BeFalse();
        sut.CustomRangeError.Should().NotBeNullOrWhiteSpace();

        var filter = sut.ToQueryFilter(1, 60);
        filter.StartDateUtc.Should().BeNull();
        filter.EndDateUtc.Should().BeNull();
    }

    [Fact]
    public void ClearAll_ResetsPresetDatesStatusesAndClasses()
    {
        var sut = new HistoryFilterViewModel { SelectedPreset = HistoryDatePreset.Day };
        sut.StatusOptions[0].IsSelected = true;
        sut.ClassOptions[0].IsSelected = true;
        sut.CustomStartDate = DateTime.Today;
        sut.CustomEndDate = DateTime.Today;

        sut.ClearAll();

        sut.SelectedPreset.Should().Be(HistoryDatePreset.None);
        sut.CustomStartDate.Should().BeNull();
        sut.CustomEndDate.Should().BeNull();
        sut.SelectedStatuses.Should().BeEmpty();
        sut.SelectedClasses.Should().BeEmpty();
        sut.HasActiveFilters.Should().BeFalse();
        sut.IsCustomRangeValid.Should().BeTrue();
    }

    [Fact]
    public void FilterChips_ReportFriendlyLabels()
    {
        var sut = new HistoryFilterViewModel();

        sut.StatusFilterLabel.Should().Be("All statuses");
        sut.ClassFilterLabel.Should().Be("All classes");

        sut.StatusOptions.Single(option => option.Value == InspectionStatus.Pass).IsSelected = true;
        sut.StatusFilterLabel.Should().Be("PASS");

        sut.StatusOptions.Single(option => option.Value == InspectionStatus.Fail).IsSelected = true;
        sut.StatusFilterLabel.Should().Be("PASS & FAIL");

        sut.ClassOptions.Single(option => option.Value == DefectClass.Short).IsSelected = true;
        sut.ClassFilterLabel.Should().Be("SH");

        sut.ClassOptions.Single(option => option.Value == DefectClass.Open).IsSelected = true;
        sut.ClassFilterLabel.Should().Be("2 classes");
    }

    [Fact]
    public void FilterChanges_RaiseFiltersChanged()
    {
        var sut = new HistoryFilterViewModel();
        int changes = 0;
        sut.FiltersChanged += (_, _) => changes++;

        sut.SelectedPreset = HistoryDatePreset.Day;
        sut.StatusOptions.Single(option => option.Value == InspectionStatus.Pass).IsSelected = true;
        sut.ClassOptions.Single(option => option.Value == DefectClass.Spur).IsSelected = true;

        changes.Should().BeGreaterThanOrEqualTo(3);
    }
}
