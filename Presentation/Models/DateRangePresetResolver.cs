using System;
using System.Globalization;

namespace pcb_deftek.Presentation.Models;

/// <summary>
/// Inclusive UTC bounds derived from a local calendar date range.
/// </summary>
public readonly record struct DateRangeBounds(DateTime? StartUtc, DateTime? EndUtc)
{
    /// <summary>Gets an unbounded all-time range.</summary>
    public static DateRangeBounds AllTime => default;
}

/// <summary>
/// Single source of truth for local calendar preset/custom range calculations.
/// </summary>
public static class DateRangePresetResolver
{
    /// <summary>
    /// Validates a custom local date range. Start and end dates are inclusive calendar days.
    /// </summary>
    public static bool TryValidateCustomRange(DateTime? start, DateTime? end, out string? error)
    {
        bool hasBoth = start.HasValue && end.HasValue;
        if (hasBoth && start!.Value.Date > end!.Value.Date)
        {
            error = "Start date must be on or before the end date.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Resolves inclusive local calendar bounds converted to UTC.
    /// Invalid or incomplete custom ranges produce <see cref="DateRangeBounds.AllTime"/>.
    /// </summary>
    public static DateRangeBounds Resolve(
        DateRangePreset preset,
        DateTime? customStart,
        DateTime? customEnd,
        DateTime today,
        CultureInfo? culture = null)
    {
        if (preset == DateRangePreset.None)
        {
            return DateRangeBounds.AllTime;
        }

        if (preset == DateRangePreset.Custom)
        {
            if (!TryValidateCustomRange(customStart, customEnd, out _) ||
                !customStart.HasValue ||
                !customEnd.HasValue)
            {
                return DateRangeBounds.AllTime;
            }

            return new DateRangeBounds(
                LocalStartOfDayUtc(customStart.Value),
                LocalEndOfDayUtc(customEnd.Value));
        }

        DateTime start;
        DateTime end;
        switch (preset)
        {
            case DateRangePreset.Day:
                start = today.Date;
                end = today.Date;
                break;

            case DateRangePreset.Week:
                CultureInfo activeCulture = culture ?? CultureInfo.CurrentCulture;
                int firstDay = (int)activeCulture.DateTimeFormat.FirstDayOfWeek;
                int offset = ((int)today.DayOfWeek - firstDay + 7) % 7;
                start = today.Date.AddDays(-offset);
                end = start.AddDays(6);
                break;

            case DateRangePreset.Month:
                start = new DateTime(today.Year, today.Month, 1);
                end = start.AddMonths(1).AddDays(-1);
                break;

            default:
                return DateRangeBounds.AllTime;
        }

        return new DateRangeBounds(LocalStartOfDayUtc(start), LocalEndOfDayUtc(end));
    }

    private static DateTime LocalStartOfDayUtc(DateTime date) =>
        DateTime.SpecifyKind(date.Date, DateTimeKind.Local).ToUniversalTime();

    private static DateTime LocalEndOfDayUtc(DateTime date) =>
        DateTime.SpecifyKind(date.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local).ToUniversalTime();
}
