using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using Xunit;

namespace pcb_deftek.Tests;

public class DashboardStatisticsTests
{
    [Fact]
    public void Statistics_DerivesTotalsFromBuckets()
    {
        var buckets = new[]
        {
            new DashboardBucket(new DateTime(2026, 1, 1), new DateTime(2026, 1, 2), "1 Jan", 3, 4, 2, 1),
            new DashboardBucket(new DateTime(2026, 1, 2), new DateTime(2026, 1, 3), "2 Jan", 2, 1, 0, 2)
        };
        var classCounts = DefectKnowledgeBase.GetAll()
            .Select(info => new DashboardClassCount(info.Class, info.Abbreviation, 0))
            .ToArray();

        var statistics = new DashboardStatistics(
            DashboardBucketGranularity.Day,
            buckets,
            classCounts);

        statistics.TotalInspections.Should().Be(5);
        statistics.TotalDefects.Should().Be(5);
        statistics.PassCount.Should().Be(2);
        statistics.FailCount.Should().Be(3);
        statistics.HasData.Should().BeTrue();
        statistics.Buckets.Should().HaveCount(2);
    }

    [Fact]
    public void EmptyStatistics_HasNoDataAndNineClassSlots()
    {
        var statistics = DashboardStatistics.Empty;

        statistics.HasData.Should().BeFalse();
        statistics.TotalInspections.Should().Be(0);
        statistics.Buckets.Should().BeEmpty();
        statistics.ClassCounts.Should().HaveCount(9);
    }

    [Theory]
    [InlineData(2, DashboardBucketGranularity.Hour)]
    [InlineData(3, DashboardBucketGranularity.Day)]
    [InlineData(90, DashboardBucketGranularity.Day)]
    [InlineData(91, DashboardBucketGranularity.Week)]
    [InlineData(730, DashboardBucketGranularity.Week)]
    [InlineData(731, DashboardBucketGranularity.Month)]
    public void Policy_ResolvesAdaptiveGranularity(int spanDays, DashboardBucketGranularity expected)
    {
        var start = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Local);
        var end = start.AddDays(spanDays);

        DashboardBucketPolicy.Resolve(
            start.ToUniversalTime(),
            end.ToUniversalTime(),
            firstInspectionUtc: null,
            lastInspectionUtc: null).Should().Be(expected);
    }

    [Fact]
    public void Policy_UsesDataExtentWhenBoundsAreMissing()
    {
        var startLocal = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Local);
        var endLocal = startLocal.AddDays(20);

        DashboardBucketPolicy.Resolve(
            startUtc: null,
            endUtc: null,
            firstInspectionUtc: startLocal.ToUniversalTime(),
            lastInspectionUtc: endLocal.ToUniversalTime()).Should().Be(DashboardBucketGranularity.Day);
    }

    [Fact]
    public void Policy_WithNoDataFallsBackToDay()
    {
        DashboardBucketPolicy.Resolve(null, null, null, null)
            .Should().Be(DashboardBucketGranularity.Day);
    }

    [Fact]
    public void Policy_WeekBucketHonorsCultureFirstDay()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        var wednesday = new DateTime(2026, 1, 7, 12, 0, 0, DateTimeKind.Local);

        var start = DashboardBucketPolicy.GetBucketStart(
            wednesday,
            DashboardBucketGranularity.Week,
            culture);

        start.DayOfWeek.Should().Be(DayOfWeek.Sunday);
        start.Date.Should().Be(new DateTime(2026, 1, 4));
    }

    [Fact]
    public void Policy_NextBucketAdvancesByGranularity()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);

        DashboardBucketPolicy.GetNextBucketStart(start, DashboardBucketGranularity.Hour)
            .Should().Be(start.AddHours(1));
        DashboardBucketPolicy.GetNextBucketStart(start, DashboardBucketGranularity.Day)
            .Should().Be(start.AddDays(1));
        DashboardBucketPolicy.GetNextBucketStart(start, DashboardBucketGranularity.Week)
            .Should().Be(start.AddDays(7));
        DashboardBucketPolicy.GetNextBucketStart(start, DashboardBucketGranularity.Month)
            .Should().Be(start.AddMonths(1));
    }
}
