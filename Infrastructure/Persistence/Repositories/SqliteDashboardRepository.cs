using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Infrastructure.Persistence.Repositories;

/// <summary>
/// SQLite dashboard aggregate reader. Inspections are loaded once with their matching defect count,
/// then projected into adaptive local buckets; class distribution is grouped in SQLite.
/// </summary>
public sealed class SqliteDashboardRepository : IDashboardRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteDashboardRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <inheritdoc />
    public async Task<DashboardStatistics> GetStatisticsAsync(
        DashboardFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<string>? allowedCodes = ResolveAllowedClassCodes(filter);
        if (filter.HasDefectFilters && allowedCodes is { Count: 0 })
        {
            return DashboardStatistics.Empty;
        }

        await using var connection = await _connectionFactory
            .CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var rows = await LoadInspectionRowsAsync(connection, filter, allowedCodes, cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return DashboardStatistics.Empty;
        }

        var classCounts = await LoadClassCountsAsync(connection, filter, allowedCodes, cancellationToken)
            .ConfigureAwait(false);

        return BuildStatistics(filter, rows, classCounts);
    }

    private static DashboardStatistics BuildStatistics(
        DashboardFilter filter,
        IReadOnlyList<InspectionRow> rows,
        IReadOnlyList<DashboardClassCount> classCounts)
    {
        DateTime minUtc = rows.Min(row => row.CreatedAtUtc);
        DateTime maxUtc = rows.Max(row => row.CreatedAtUtc);
        DashboardBucketGranularity granularity = DashboardBucketPolicy.Resolve(
            filter.StartDateUtc,
            filter.EndDateUtc,
            minUtc,
            maxUtc,
            filter.BucketGranularityOverride);

        CultureInfo culture = CultureInfo.CurrentCulture;
        DateTime rangeStartUtc = filter.StartDateUtc ?? minUtc;
        DateTime rangeEndUtc = filter.EndDateUtc ?? maxUtc;
        DateTime firstBucket = DashboardBucketPolicy.GetBucketStart(
            rangeStartUtc.ToLocalTime(), granularity, culture);
        DateTime lastBucket = DashboardBucketPolicy.GetBucketStart(
            rangeEndUtc.ToLocalTime(), granularity, culture);

        var bucketStarts = new List<DateTime>();
        var inspections = new List<int>();
        var defects = new List<int>();
        var passes = new List<int>();
        var fails = new List<int>();

        for (DateTime cursor = firstBucket; cursor <= lastBucket; cursor = DashboardBucketPolicy.GetNextBucketStart(cursor, granularity))
        {
            bucketStarts.Add(cursor);
            inspections.Add(0);
            defects.Add(0);
            passes.Add(0);
            fails.Add(0);
        }

        var indexByBucket = bucketStarts
            .Select((start, index) => (start, index))
            .ToDictionary(item => item.start, item => item.index);

        foreach (var row in rows)
        {
            DateTime key = DashboardBucketPolicy.GetBucketStart(
                row.CreatedAtUtc.ToLocalTime(), granularity, culture);

            if (!indexByBucket.TryGetValue(key, out int index))
            {
                continue;
            }

            inspections[index]++;
            defects[index] += row.MatchingDefects;
            if (row.DefectCount == 0)
            {
                passes[index]++;
            }
            else
            {
                fails[index]++;
            }
        }

        var buckets = new List<DashboardBucket>(bucketStarts.Count);
        for (int i = 0; i < bucketStarts.Count; i++)
        {
            DateTime start = bucketStarts[i];
            DateTime end = DashboardBucketPolicy.GetNextBucketStart(start, granularity);
            buckets.Add(new DashboardBucket(
                start,
                end,
                DashboardBucketPolicy.GetLabel(start, granularity, culture),
                inspections[i],
                defects[i],
                passes[i],
                fails[i]));
        }

        return new DashboardStatistics(granularity, buckets, classCounts);
    }

    private static async Task<IReadOnlyList<InspectionRow>> LoadInspectionRowsAsync(
        SqliteConnection connection,
        DashboardFilter filter,
        IReadOnlyList<string>? allowedCodes,
        CancellationToken cancellationToken)
    {
        var sql = new StringBuilder();
        if (filter.HasDefectFilters)
        {
            sql.AppendLine(@"
                SELECT i.Id, i.CreatedAtUtc, i.DefectCount, COUNT(d.Id)
                FROM Inspections i
                JOIN Defects d ON d.InspectionId = i.Id
                    AND d.ClassCode IN (");
            AppendCodeParameters(sql, allowedCodes);
            sql.AppendLine(")");
        }
        else
        {
            sql.AppendLine(@"
                SELECT i.Id, i.CreatedAtUtc, i.DefectCount, i.DefectCount
                FROM Inspections i");
        }

        sql.AppendLine(" WHERE 1=1");
        AppendDatePredicate(sql, filter);
        if (filter.HasDefectFilters)
        {
            sql.AppendLine(" GROUP BY i.Id, i.CreatedAtUtc, i.DefectCount");
        }

        sql.AppendLine(" ORDER BY i.CreatedAtUtc ASC;");

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql.ToString();
        AddParameters(cmd, filter, allowedCodes);

        var rows = new List<InspectionRow>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new InspectionRow(
                DateTime.Parse(reader.GetString(1), null, DateTimeStyles.RoundtripKind),
                reader.GetInt32(2),
                Convert.ToInt32(reader.GetInt64(3))));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<DashboardClassCount>> LoadClassCountsAsync(
        SqliteConnection connection,
        DashboardFilter filter,
        IReadOnlyList<string>? allowedCodes,
        CancellationToken cancellationToken)
    {
        var sql = new StringBuilder();
        sql.AppendLine(@"
            SELECT d.ClassCode, COUNT(1)
            FROM Defects d
            JOIN Inspections i ON i.Id = d.InspectionId
            WHERE 1=1");
        AppendDatePredicate(sql, filter);
        if (allowedCodes is not null)
        {
            sql.Append(" AND d.ClassCode IN (");
            AppendCodeParameters(sql, allowedCodes);
            sql.AppendLine(")");
        }

        sql.AppendLine(" GROUP BY d.ClassCode;");

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql.ToString();
        AddParameters(cmd, filter, allowedCodes);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                counts[reader.GetString(0)] = Convert.ToInt32(reader.GetInt64(1));
            }
        }

        return DefectKnowledgeBase.GetAll()
            .OrderBy(info => info.ClassId)
            .Select(info => new DashboardClassCount(
                info.Class,
                info.Abbreviation,
                counts.GetValueOrDefault(info.Code)))
            .ToArray();
    }

    private static IReadOnlyList<string>? ResolveAllowedClassCodes(DashboardFilter filter)
    {
        if (!filter.HasDefectFilters)
        {
            return null;
        }

        IEnumerable<DefectClassInfo> infos = filter.DefectClasses.Count > 0
            ? filter.DefectClasses.Select(DefectKnowledgeBase.GetInfo)
            : DefectKnowledgeBase.GetAll();

        if (filter.Severities.Count > 0)
        {
            var severities = filter.Severities.ToHashSet();
            infos = infos.Where(info => severities.Contains(info.Severity));
        }

        return infos
            .Select(info => info.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AppendDatePredicate(StringBuilder sql, DashboardFilter filter)
    {
        if (filter.StartDateUtc is not null)
        {
            sql.AppendLine(" AND i.CreatedAtUtc >= @StartDateUtc");
        }

        if (filter.EndDateUtc is not null)
        {
            sql.AppendLine(" AND i.CreatedAtUtc <= @EndDateUtc");
        }
    }

    private static void AppendCodeParameters(StringBuilder sql, IReadOnlyList<string>? allowedCodes)
    {
        allowedCodes ??= Array.Empty<string>();
        for (int i = 0; i < allowedCodes.Count; i++)
        {
            if (i > 0)
            {
                sql.Append(", ");
            }

            sql.Append('@').Append('C').Append(i);
        }
    }

    private static void AddParameters(
        SqliteCommand cmd,
        DashboardFilter filter,
        IReadOnlyList<string>? allowedCodes)
    {
        if (filter.StartDateUtc is { } start)
        {
            cmd.Parameters.AddWithValue("@StartDateUtc", start.ToString("o"));
        }

        if (filter.EndDateUtc is { } end)
        {
            cmd.Parameters.AddWithValue("@EndDateUtc", end.ToString("o"));
        }

        if (allowedCodes is not null)
        {
            for (int i = 0; i < allowedCodes.Count; i++)
            {
                cmd.Parameters.AddWithValue($"@C{i}", allowedCodes[i]);
            }
        }
    }

    private sealed record InspectionRow(DateTime CreatedAtUtc, int DefectCount, int MatchingDefects);
}
