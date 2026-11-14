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

    /// <inheritdoc />
    public async Task<IReadOnlyList<InspectionRecord>> GetMatchingInspectionsAsync(
        DashboardFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<string>? allowedCodes = ResolveAllowedClassCodes(filter);
        if (filter.HasDefectFilters && allowedCodes is { Count: 0 })
        {
            return Array.Empty<InspectionRecord>();
        }

        await using var connection = await _connectionFactory
            .CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var rows = await LoadEvidenceRowsAsync(connection, filter, allowedCodes, cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => new InspectionRecord(
                row.Id,
                row.Name,
                row.CreatedAtUtc,
                row.UpdatedAtUtc,
                row.Mode,
                row.ImagePath,
                row.Defects,
                row.Origin))
            .ToArray();
    }

    private static async Task<IReadOnlyList<EvidenceRow>> LoadEvidenceRowsAsync(
        SqliteConnection connection,
        DashboardFilter filter,
        IReadOnlyList<string>? allowedCodes,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = BuildEvidenceSql(filter, allowedCodes);
        AddParameters(cmd, filter, allowedCodes);

        var rows = new List<EvidenceRow>();
        var rowsById = new Dictionary<Guid, EvidenceRow>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            Guid inspectionId = Guid.Parse(reader.GetString(0));
            if (!rowsById.TryGetValue(inspectionId, out EvidenceRow? row))
            {
                row = new EvidenceRow(
                    inspectionId,
                    reader.GetString(1),
                    DateTime.Parse(reader.GetString(2), null, DateTimeStyles.RoundtripKind),
                    DateTime.Parse(reader.GetString(3), null, DateTimeStyles.RoundtripKind),
                    (DetectionMode)reader.GetInt32(4),
                    reader.GetString(5),
                    (InspectionOrigin)reader.GetInt32(6));
                rowsById[inspectionId] = row;
                rows.Add(row);
            }

            if (reader.IsDBNull(7))
            {
                continue;
            }

            row.Defects.Add(new DetectedDefect(
                Guid.Parse(reader.GetString(7)),
                inspectionId,
                reader.GetString(8),
                (float)reader.GetDouble(9),
                new BoundingBox(
                    (float)reader.GetDouble(10),
                    (float)reader.GetDouble(11),
                    (float)reader.GetDouble(12),
                    (float)reader.GetDouble(13))));
        }

        return rows;
    }

    private static string BuildEvidenceSql(DashboardFilter filter, IReadOnlyList<string>? allowedCodes)
    {
        var sql = new StringBuilder();
        sql.AppendLine(@"
            SELECT i.Id, i.Name, i.CreatedAtUtc, i.UpdatedAtUtc, i.DetectionMode, i.ImagePath, i.Origin,
                   d.Id, d.ClassCode, d.Confidence, d.BoxX, d.BoxY, d.BoxWidth, d.BoxHeight
            FROM Inspections i");

        if (allowedCodes is not null)
        {
            sql.Append(" LEFT JOIN Defects d ON d.InspectionId = i.Id AND d.ClassCode IN (");
            AppendCodeParameters(sql, allowedCodes);
            sql.AppendLine(")");
        }
        else
        {
            sql.AppendLine(" LEFT JOIN Defects d ON d.InspectionId = i.Id");
        }

        sql.AppendLine(" WHERE 1=1");
        AppendDatePredicate(sql, filter);
        AppendDefectFilterPredicate(sql, allowedCodes);
        sql.AppendLine(" ORDER BY i.CreatedAtUtc DESC, i.Id DESC, d.ClassCode ASC, d.Confidence DESC, d.Id ASC;");
        return sql.ToString();
    }

    /// <inheritdoc />
    public async Task<int> GetMatchingInspectionCountAsync(
        DashboardFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<string>? allowedCodes = ResolveAllowedClassCodes(filter);
        if (filter.HasDefectFilters && allowedCodes is { Count: 0 })
        {
            return 0;
        }

        await using var connection = await _connectionFactory
            .CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var sql = new StringBuilder("SELECT COUNT(1) FROM Inspections i WHERE 1=1");
        AppendDatePredicate(sql, filter);
        AppendDefectFilterPredicate(sql, allowedCodes);
        sql.Append(';');

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql.ToString();
        AddParameters(cmd, filter, allowedCodes);

        object? result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result);
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

    private static void AppendDefectFilterPredicate(
        StringBuilder sql,
        IReadOnlyList<string>? allowedCodes)
    {
        if (allowedCodes is null)
        {
            return;
        }

        sql.Append(" AND EXISTS (SELECT 1 FROM Defects md WHERE md.InspectionId = i.Id AND md.ClassCode IN (");
        AppendCodeParameters(sql, allowedCodes);
        sql.Append("))");
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

    private sealed class EvidenceRow
    {
        public EvidenceRow(
            Guid id,
            string name,
            DateTime createdAtUtc,
            DateTime updatedAtUtc,
            DetectionMode mode,
            string imagePath,
            InspectionOrigin origin)
        {
            Id = id;
            Name = name;
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
            Mode = mode;
            ImagePath = imagePath;
            Origin = origin;
        }

        public Guid Id { get; }

        public string Name { get; }

        public DateTime CreatedAtUtc { get; }

        public DateTime UpdatedAtUtc { get; }

        public DetectionMode Mode { get; }

        public string ImagePath { get; }

        public InspectionOrigin Origin { get; }

        public List<DetectedDefect> Defects { get; } = new();
    }
}
