using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Domain.Contracts;

/// <summary>
/// Filter applied to the dashboard aggregate query. Date bounds are inclusive UTC instants;
/// empty defect-class or severity collections mean no restriction in that category.
/// </summary>
public sealed class DashboardFilter
{
    public DashboardFilter(
        DateTime? startDateUtc = null,
        DateTime? endDateUtc = null,
        IEnumerable<DefectClass>? defectClasses = null,
        IEnumerable<DefectSeverity>? severities = null,
        DashboardBucketGranularity? bucketGranularityOverride = null)
    {
        StartDateUtc = startDateUtc?.ToUniversalTime();
        BucketGranularityOverride = bucketGranularityOverride;
        EndDateUtc = endDateUtc?.ToUniversalTime();

        if (StartDateUtc is { } start && EndDateUtc is { } end && start > end)
        {
            throw new ArgumentException("Dashboard start date must not be after the end date.", nameof(startDateUtc));
        }

        DefectClasses = defectClasses?.Distinct().ToArray() ?? Array.Empty<DefectClass>();
        Severities = severities?.Distinct().ToArray() ?? Array.Empty<DefectSeverity>();
    }

    /// <summary>Gets the inclusive start instant, or null for all time.</summary>
    public DateTime? StartDateUtc { get; }

    /// <summary>Gets the inclusive end instant, or null for all time.</summary>
    public DateTime? EndDateUtc { get; }

    /// <summary>Gets the selected defect classes.</summary>
    public IReadOnlyList<DefectClass> DefectClasses { get; }

    /// <summary>Gets the selected defect severities.</summary>
    public IReadOnlyList<DefectSeverity> Severities { get; }

    /// <summary>Gets an optional bucket granularity imposed by the caller.</summary>
    public DashboardBucketGranularity? BucketGranularityOverride { get; }

    /// <summary>Gets a value indicating whether a class or severity restriction is active.</summary>
    public bool HasDefectFilters => Severities.Count > 0 ||
        (DefectClasses.Count > 0 && DefectClasses.Count < DefectKnowledgeBase.GetAll().Count);

    /// <summary>Gets an unfiltered all-time filter.</summary>
    public static DashboardFilter AllTime => new();
}

/// <summary>Granularity used for dashboard time-series buckets.</summary>
public enum DashboardBucketGranularity
{
    Hour = 0,
    Day = 1,
    Week = 2,
    Month = 3
}

/// <summary>One adaptive time-series bucket in local time.</summary>
public sealed record DashboardBucket(
    DateTime StartLocal,
    DateTime EndLocal,
    string Label,
    int Inspections,
    int Defects,
    int Passes,
    int Fails);

/// <summary>Matching defect count for one class.</summary>
public sealed record DashboardClassCount(DefectClass Class, string Abbreviation, int Count);

/// <summary>Filtered dashboard aggregates and their chart projections.</summary>
public sealed record DashboardStatistics(
    DashboardBucketGranularity BucketGranularity,
    IReadOnlyList<DashboardBucket> Buckets,
    IReadOnlyList<DashboardClassCount> ClassCounts)
{
    /// <summary>Gets total included inspections.</summary>
    public int TotalInspections => Buckets.Sum(bucket => bucket.Inspections);

    /// <summary>Gets total matching defects.</summary>
    public int TotalDefects => Buckets.Sum(bucket => bucket.Defects);

    /// <summary>Gets total included PASS inspections.</summary>
    public int PassCount => Buckets.Sum(bucket => bucket.Passes);

    /// <summary>Gets total included FAIL inspections.</summary>
    public int FailCount => Buckets.Sum(bucket => bucket.Fails);

    /// <summary>Gets a value indicating whether any inspection matched.</summary>
    public bool HasData => Buckets.Any(bucket => bucket.Inspections > 0);

    /// <summary>Gets an empty daily result with all nine class slots.</summary>
    public static DashboardStatistics Empty => new(
        DashboardBucketGranularity.Day,
        Array.Empty<DashboardBucket>(),
        DefectKnowledgeBase.GetAll()
            .OrderBy(info => info.ClassId)
            .Select(info => new DashboardClassCount(info.Class, info.Abbreviation, 0))
            .ToArray());
}

/// <summary>
/// Resolves the adaptive time bucket and performs local bucket/label calculations.
/// </summary>
public static class DashboardBucketPolicy
{
    public static DashboardBucketGranularity Resolve(
        DateTime? startUtc,
        DateTime? endUtc,
        DateTime? firstInspectionUtc,
        DateTime? lastInspectionUtc,
        DashboardBucketGranularity? granularityOverride = null)
    {
        if (granularityOverride is { } overridden)
        {
            return overridden;
        }

        DateTime? start = startUtc ?? firstInspectionUtc;
        DateTime? end = endUtc ?? lastInspectionUtc;

        if (start is null || end is null || end < start)
        {
            return DashboardBucketGranularity.Day;
        }

        TimeSpan span = end.Value.ToLocalTime() - start.Value.ToLocalTime();
        if (span <= TimeSpan.FromDays(2))
        {
            return DashboardBucketGranularity.Hour;
        }

        if (span <= TimeSpan.FromDays(90))
        {
            return DashboardBucketGranularity.Day;
        }

        return span <= TimeSpan.FromDays(730)
            ? DashboardBucketGranularity.Week
            : DashboardBucketGranularity.Month;
    }

    public static DateTime GetBucketStart(
        DateTime local,
        DashboardBucketGranularity granularity,
        CultureInfo culture)
    {
        return granularity switch
        {
            DashboardBucketGranularity.Hour => new DateTime(
                local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Kind),
            DashboardBucketGranularity.Day => new DateTime(
                local.Year, local.Month, local.Day, 0, 0, 0, local.Kind),
            DashboardBucketGranularity.Week => GetWeekStart(local, culture),
            DashboardBucketGranularity.Month => new DateTime(
                local.Year, local.Month, 1, 0, 0, 0, local.Kind),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity))
        };
    }

    public static DateTime GetNextBucketStart(
        DateTime start,
        DashboardBucketGranularity granularity)
    {
        return granularity switch
        {
            DashboardBucketGranularity.Hour => start.AddHours(1),
            DashboardBucketGranularity.Day => start.AddDays(1),
            DashboardBucketGranularity.Week => start.AddDays(7),
            DashboardBucketGranularity.Month => start.AddMonths(1),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity))
        };
    }

    public static string GetLabel(
        DateTime start,
        DashboardBucketGranularity granularity,
        CultureInfo culture)
    {
        return granularity switch
        {
            DashboardBucketGranularity.Hour => start.ToString("HH:00", culture),
            DashboardBucketGranularity.Day => start.ToString("d MMM", culture),
            DashboardBucketGranularity.Week => start.ToString("d MMM", culture),
            DashboardBucketGranularity.Month => start.ToString("MMM yyyy", culture),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity))
        };
    }

    private static DateTime GetWeekStart(DateTime local, CultureInfo culture)
    {
        int firstDay = (int)culture.DateTimeFormat.FirstDayOfWeek;
        int currentDay = (int)local.DayOfWeek;
        int offset = (currentDay - firstDay + 7) % 7;
        DateTime date = local.Date.AddDays(-offset);
        return new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, local.Kind);
    }
}
