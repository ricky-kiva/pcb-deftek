using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Presentation.Models;
using Xunit;

namespace pcb_deftek.Tests;

public class HistoryGroupingTests
{
    private static InspectionSummary CreateSummary(string name, DateTime createdAtUtc)
    {
        return new InspectionSummary(
            Guid.NewGuid(),
            name,
            createdAtUtc,
            createdAtUtc,
            DetectionMode.SingleFile,
            0,
            $"images/{name}.png");
    }

    private static DateTime LocalNoonUtc(int dayOffset = 0)
    {
        var localNoon = DateTime.SpecifyKind(
            DateTime.Today.AddDays(dayOffset).AddHours(12),
            DateTimeKind.Local);

        return localNoon.ToUniversalTime();
    }

    [Fact]
    public void Group_WithEmptyInput_ReturnsEmptyCollection()
    {
        var groups = HistoryGrouping.Group(Array.Empty<InspectionSummary>());

        groups.Should().BeEmpty();
    }

    [Fact]
    public void Group_WithNullInput_ThrowsArgumentNullException()
    {
        Action act = () => HistoryGrouping.Group(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Group_GroupsByLocalCalendarDateAndOrdersGroupsNewestFirst()
    {
        var newest = CreateSummary("newest", LocalNoonUtc());
        var oldest = CreateSummary("oldest", LocalNoonUtc(-3));
        var middle = CreateSummary("middle", LocalNoonUtc(-1));

        var groups = HistoryGrouping.Group(new[] { oldest, newest, middle });

        groups.Select(group => group.Date).Should().BeInDescendingOrder();
        groups.Should().HaveCount(3);
        groups[0].Items.Should().ContainSingle().Which.Id.Should().Be(newest.Id);
    }

    [Fact]
    public void Group_MergesSummariesFromTheSameLocalDate()
    {
        var first = CreateSummary("first", LocalNoonUtc());
        var second = CreateSummary("second", LocalNoonUtc().AddHours(-1));

        var groups = HistoryGrouping.Group(new[] { first, second });

        groups.Should().ContainSingle();
        groups[0].Items.Select(item => item.Id).Should().ContainInOrder(first.Id, second.Id);
    }

    [Fact]
    public void Group_PreservesInputOrderWithinEachGroup()
    {
        var first = CreateSummary("first", LocalNoonUtc());
        var second = CreateSummary("second", LocalNoonUtc().AddMinutes(-5));
        var third = CreateSummary("third", LocalNoonUtc().AddMinutes(-10));

        var groups = HistoryGrouping.Group(new[] { first, second, third });

        groups[0].Items.Select(item => item.Id).Should().ContainInOrder(first.Id, second.Id, third.Id);
    }

    [Fact]
    public void Group_ProducesCultureAwareNonEmptyLabelForEachGroup()
    {
        var groups = HistoryGrouping.Group(new[] { CreateSummary("board", LocalNoonUtc()) });

        groups[0].Label.Should().NotBeNullOrWhiteSpace();
        groups[0].Label.Should().Be(groups[0].Date.ToString("D", System.Globalization.CultureInfo.CurrentCulture));
    }

    [Fact]
    public void Group_UsesTheSameLocalDateAsTheSummaryTimestamp()
    {
        var localDate = new DateTime(2026, 11, 13, 23, 30, 0, DateTimeKind.Local);
        var summary = CreateSummary("late", localDate.ToUniversalTime());

        var groups = HistoryGrouping.Group(new[] { summary });

        groups.Should().ContainSingle();
        groups[0].Date.Should().Be(localDate.Date);
    }
}
