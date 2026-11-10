using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Infrastructure.Persistence.Repositories;

/// <summary>
/// SQLite implementation of <see cref="IDefectRepository"/> for storing and aggregating defect analytics.
/// </summary>
public class SqliteDefectRepository : IDefectRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteDefectRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <inheritdoc />
    public async Task InsertBatchAsync(Guid inspectionId, IReadOnlyList<DetectedDefect> defects, CancellationToken cancellationToken = default)
    {
        if (inspectionId == Guid.Empty) throw new ArgumentException("Inspection ID cannot be empty.", nameof(inspectionId));
        ArgumentNullException.ThrowIfNull(defects);

        if (defects.Count == 0) return;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            const string insertSql = @"
                INSERT INTO Defects (Id, InspectionId, ClassCode, Confidence, BoxX, BoxY, BoxWidth, BoxHeight)
                VALUES (@Id, @InspectionId, @ClassCode, @Confidence, @BoxX, @BoxY, @BoxWidth, @BoxHeight);
            ";

            foreach (var defect in defects)
            {
                await using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = insertSql;
                cmd.Parameters.AddWithValue("@Id", defect.Id.ToString("D"));
                cmd.Parameters.AddWithValue("@InspectionId", inspectionId.ToString("D"));
                cmd.Parameters.AddWithValue("@ClassCode", defect.ClassCode);
                cmd.Parameters.AddWithValue("@Confidence", defect.Confidence);
                cmd.Parameters.AddWithValue("@BoxX", defect.BoundingBox.X);
                cmd.Parameters.AddWithValue("@BoxY", defect.BoundingBox.Y);
                cmd.Parameters.AddWithValue("@BoxWidth", defect.BoundingBox.Width);
                cmd.Parameters.AddWithValue("@BoxHeight", defect.BoundingBox.Height);

                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // Update cached DefectCount on parent inspection
            const string updateInspectionCountSql = @"
                UPDATE Inspections
                SET DefectCount = (SELECT COUNT(1) FROM Defects WHERE InspectionId = @InspectionId),
                    UpdatedAtUtc = @UpdatedAtUtc
                WHERE Id = @InspectionId;
            ";

            await using (var countCmd = connection.CreateCommand())
            {
                countCmd.Transaction = transaction;
                countCmd.CommandText = updateInspectionCountSql;
                countCmd.Parameters.AddWithValue("@InspectionId", inspectionId.ToString("D"));
                countCmd.Parameters.AddWithValue("@UpdatedAtUtc", DateTime.UtcNow.ToString("o"));
                await countCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw new PersistenceException($"Failed to insert defect batch for inspection '{inspectionId}'.", _connectionFactory.DatabasePath, ex);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DetectedDefect>> GetByInspectionIdAsync(Guid inspectionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        const string selectSql = @"
            SELECT Id, ClassCode, Confidence, BoxX, BoxY, BoxWidth, BoxHeight
            FROM Defects
            WHERE InspectionId = @InspectionId;
        ";

        var list = new List<DetectedDefect>();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = selectSql;
        cmd.Parameters.AddWithValue("@InspectionId", inspectionId.ToString("D"));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = Guid.Parse(reader.GetString(0));
            var classCode = reader.GetString(1);
            var confidence = (float)reader.GetDouble(2);
            var boxX = (float)reader.GetDouble(3);
            var boxY = (float)reader.GetDouble(4);
            var boxW = (float)reader.GetDouble(5);
            var boxH = (float)reader.GetDouble(6);

            var box = new BoundingBox(boxX, boxY, boxW, boxH);
            list.Add(new DetectedDefect(id, inspectionId, classCode, confidence, box));
        }

        return list;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, int>> GetClassDistributionAsync(DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var query = new StringBuilder(@"
            SELECT d.ClassCode, COUNT(1)
            FROM Defects d
            JOIN Inspections i ON d.InspectionId = i.Id
            WHERE 1=1
        ");

        var parameters = new List<SqliteParameter>();

        if (fromUtc.HasValue)
        {
            query.Append(" AND i.CreatedAtUtc >= @FromUtc");
            parameters.Add(new SqliteParameter("@FromUtc", fromUtc.Value.ToString("o")));
        }

        if (toUtc.HasValue)
        {
            query.Append(" AND i.CreatedAtUtc <= @ToUtc");
            parameters.Add(new SqliteParameter("@ToUtc", toUtc.Value.ToString("o")));
        }

        query.Append(" GROUP BY d.ClassCode;");

        var distribution = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = query.ToString();
        foreach (var p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string code = reader.GetString(0);
            int count = reader.GetInt32(1);
            distribution[code] = count;
        }

        return distribution;
    }

    /// <inheritdoc />
    public async Task<int> GetTotalDefectCountAsync(DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var query = new StringBuilder(@"
            SELECT COUNT(1)
            FROM Defects d
            JOIN Inspections i ON d.InspectionId = i.Id
            WHERE 1=1
        ");

        var parameters = new List<SqliteParameter>();

        if (fromUtc.HasValue)
        {
            query.Append(" AND i.CreatedAtUtc >= @FromUtc");
            parameters.Add(new SqliteParameter("@FromUtc", fromUtc.Value.ToString("o")));
        }

        if (toUtc.HasValue)
        {
            query.Append(" AND i.CreatedAtUtc <= @ToUtc");
            parameters.Add(new SqliteParameter("@ToUtc", toUtc.Value.ToString("o")));
        }

        query.Append(';');

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = query.ToString();
        foreach (var p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }
}
