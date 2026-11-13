using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using pcb_deftek.Domain.Contracts;

namespace pcb_deftek.Presentation.ViewModels;

/// <summary>
/// Represents a gallery section containing the inspection summaries captured on one local calendar date.
/// </summary>
/// <param name="Date">The local calendar date shared by the group's items.</param>
/// <param name="Label">Culture-aware display label for the date header.</param>
/// <param name="Items">Inspection summaries in their original repository order.</param>
public sealed record HistoryDateGroup(
    DateTime Date,
    string Label,
    IReadOnlyList<InspectionSummary> Items);

/// <summary>
/// Pure grouping logic for the history gallery. Converts UTC inspection timestamps into
/// ordered local-calendar-date sections without depending on WPF collection views.
/// </summary>
public static class HistoryGrouping
{
    /// <summary>
    /// Groups inspection summaries by their local calendar date, newest date first,
    /// preserving the input order within each group.
    /// </summary>
    /// <param name="summaries">Inspection summaries in repository order.</param>
    /// <returns>Ordered date groups; empty when no summaries are supplied.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="summaries"/> is null.</exception>
    public static IReadOnlyList<HistoryDateGroup> Group(IReadOnlyCollection<InspectionSummary> summaries)
    {
        ArgumentNullException.ThrowIfNull(summaries);

        if (summaries.Count == 0)
        {
            return Array.Empty<HistoryDateGroup>();
        }

        return summaries
            .GroupBy(summary => summary.CreatedAtUtc.ToLocalTime().Date)
            .Select(group => new HistoryDateGroup(
                group.Key,
                group.Key.ToString("D", CultureInfo.CurrentCulture),
                group.ToList()))
            .OrderByDescending(group => group.Date)
            .ToList();
    }
}
