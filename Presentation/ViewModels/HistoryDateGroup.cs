using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using pcb_deftek.Domain.Contracts;

namespace pcb_deftek.Presentation.ViewModels;

/// <summary>
/// Represents a gallery section containing the items captured on one local calendar date.
/// </summary>
/// <typeparam name="T">Item type contained in the group.</typeparam>
/// <param name="Date">The local calendar date shared by the group's items.</param>
/// <param name="Label">Culture-aware display label for the date header.</param>
/// <param name="Items">Items in their original input order.</param>
public sealed record HistoryDateGroup<T>(
    DateTime Date,
    string Label,
    IReadOnlyList<T> Items);

/// <summary>
/// Pure grouping logic for the history gallery. Converts UTC timestamps into
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
    public static IReadOnlyList<HistoryDateGroup<InspectionSummary>> Group(
        IReadOnlyCollection<InspectionSummary> summaries)
    {
        ArgumentNullException.ThrowIfNull(summaries);
        return Group(summaries, summary => summary.CreatedAtUtc);
    }

    /// <summary>
    /// Groups arbitrary items by the local calendar date of their UTC timestamp, newest date first,
    /// preserving the input order within each group.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="items">Items in display order.</param>
    /// <param name="createdAtUtcSelector">Selects each item's UTC creation timestamp.</param>
    /// <returns>Ordered date groups; empty when no items are supplied.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="items"/> or <paramref name="createdAtUtcSelector"/> is null.
    /// </exception>
    public static IReadOnlyList<HistoryDateGroup<T>> Group<T>(
        IReadOnlyCollection<T> items,
        Func<T, DateTime> createdAtUtcSelector)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(createdAtUtcSelector);

        if (items.Count == 0)
        {
            return Array.Empty<HistoryDateGroup<T>>();
        }

        return items
            .GroupBy(item => createdAtUtcSelector(item).ToLocalTime().Date)
            .Select(group => new HistoryDateGroup<T>(
                group.Key,
                group.Key.ToString("D", CultureInfo.CurrentCulture),
                group.ToList()))
            .OrderByDescending(group => group.Date)
            .ToList();
    }
}
