using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Infrastructure.Persistence.Repositories;

/// <summary>
/// SQLite implementation of <see cref="IInspectionRepository"/> with transaction management and cascading relational integrity.
/// </summary>
public class SqliteInspectionRepository : IInspectionRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteInspectionRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <inheritdoc />
    public async Task<Guid> InsertAsync(InspectionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            const string insertInspectionSql = @"
                INSERT INTO Inspections (Id, Name, CreatedAtUtc, UpdatedAtUtc, DetectionMode, DefectCount, ImagePath)
                VALUES (@Id, @Name, @CreatedAtUtc, @UpdatedAtUtc, @DetectionMode, @DefectCount, @ImagePath);
            ";

            await using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = insertInspectionSql;
                cmd.Parameters.AddWithValue("@Id", record.Id.ToString("D"));
                cmd.Parameters.AddWithValue("@Name", record.Name);
                cmd.Parameters.AddWithValue("@CreatedAtUtc", record.CreatedAtUtc.ToString("o"));
                cmd.Parameters.AddWithValue("@UpdatedAtUtc", record.UpdatedAtUtc.ToString("o"));
                cmd.Parameters.AddWithValue("@DetectionMode", (int)record.Mode);
                cmd.Parameters.AddWithValue("@DefectCount", record.DefectCount);
                cmd.Parameters.AddWithValue("@ImagePath", record.ImagePath);

                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            if (record.Defects.Count > 0)
            {
                const string insertDefectSql = @"
                    INSERT INTO Defects (Id, InspectionId, ClassCode, Confidence, BoxX, BoxY, BoxWidth, BoxHeight)
                    VALUES (@Id, @InspectionId, @ClassCode, @Confidence, @BoxX, @BoxY, @BoxWidth, @BoxHeight);
                ";

                foreach (var defect in record.Defects)
                {
                    await using var defectCmd = connection.CreateCommand();
                    defectCmd.Transaction = transaction;
                    defectCmd.CommandText = insertDefectSql;
                    defectCmd.Parameters.AddWithValue("@Id", defect.Id.ToString("D"));
                    defectCmd.Parameters.AddWithValue("@InspectionId", record.Id.ToString("D"));
                    defectCmd.Parameters.AddWithValue("@ClassCode", defect.ClassCode);
                    defectCmd.Parameters.AddWithValue("@Confidence", defect.Confidence);
                    defectCmd.Parameters.AddWithValue("@BoxX", defect.BoundingBox.X);
                    defectCmd.Parameters.AddWithValue("@BoxY", defect.BoundingBox.Y);
                    defectCmd.Parameters.AddWithValue("@BoxWidth", defect.BoundingBox.Width);
                    defectCmd.Parameters.AddWithValue("@BoxHeight", defect.BoundingBox.Height);

                    await defectCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return record.Id;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw new PersistenceException($"Failed inserting inspection record '{record.Id}'.", _connectionFactory.DatabasePath, ex);
        }
    }

    /// <inheritdoc />
    public async Task<InspectionRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        const string selectInspectionSql = @"
            SELECT Id, Name, CreatedAtUtc, UpdatedAtUtc, DetectionMode, ImagePath
            FROM Inspections
            WHERE Id = @Id;
        ";

        string? name = null;
        DateTime createdAtUtc = default;
        DateTime updatedAtUtc = default;
        DetectionMode mode = default;
        string? imagePath = null;
        bool found = false;

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = selectInspectionSql;
            cmd.Parameters.AddWithValue("@Id", id.ToString("D"));

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                name = reader.GetString(1);
                createdAtUtc = DateTime.Parse(reader.GetString(2), null, DateTimeStyles.RoundtripKind);
                updatedAtUtc = DateTime.Parse(reader.GetString(3), null, DateTimeStyles.RoundtripKind);
                mode = (DetectionMode)reader.GetInt32(4);
                imagePath = reader.GetString(5);
                found = true;
            }
        }

        if (!found || name == null || imagePath == null)
        {
            return null;
        }

        var defects = await LoadDefectsInternalAsync(connection, id, cancellationToken).ConfigureAwait(false);

        return new InspectionRecord(
            id: id,
            name: name,
            createdAtUtc: createdAtUtc,
            updatedAtUtc: updatedAtUtc,
            mode: mode,
            imagePath: imagePath,
            defects: defects);
    }

    /// <inheritdoc />
    public async Task<PagedResult<InspectionRecord>> GetPagedAsync(InspectionQueryFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var whereClause = new StringBuilder(" WHERE 1=1");
        var parameters = new List<SqliteParameter>();

        if (filter.StartDateUtc.HasValue)
        {
            whereClause.Append(" AND CreatedAtUtc >= @StartDate");
            parameters.Add(new SqliteParameter("@StartDate", filter.StartDateUtc.Value.ToString("o")));
        }

        if (filter.EndDateUtc.HasValue)
        {
            whereClause.Append(" AND CreatedAtUtc <= @EndDate");
            parameters.Add(new SqliteParameter("@EndDate", filter.EndDateUtc.Value.ToString("o")));
        }

        if (filter.Mode.HasValue)
        {
            whereClause.Append(" AND DetectionMode = @DetectionMode");
            parameters.Add(new SqliteParameter("@DetectionMode", (int)filter.Mode.Value));
        }

        if (filter.Status.HasValue)
        {
            if (filter.Status.Value == InspectionStatus.Pass)
            {
                whereClause.Append(" AND DefectCount = 0");
            }
            else
            {
                whereClause.Append(" AND DefectCount > 0");
            }
        }

        // Count total matching items
        int totalItems;
        await using (var countCmd = connection.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(1) FROM Inspections" + whereClause + ";";
            foreach (var p in parameters) countCmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));

            var countResult = await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            totalItems = Convert.ToInt32(countResult);
        }

        if (totalItems == 0)
        {
            return PagedResult<InspectionRecord>.Empty(filter.PageSize);
        }

        // Determine safe sort column
        string sortColumn = filter.SortBy?.ToLowerInvariant() switch
        {
            "name" => "Name",
            "defectcount" => "DefectCount",
            _ => "CreatedAtUtc"
        };
        string sortDirection = filter.SortDescending ? "DESC" : "ASC";

        int offset = (filter.PageNumber - 1) * filter.PageSize;

        string querySql = $@"
            SELECT Id, Name, CreatedAtUtc, UpdatedAtUtc, DetectionMode, ImagePath
            FROM Inspections
            {whereClause}
            ORDER BY {sortColumn} {sortDirection}
            LIMIT @Limit OFFSET @Offset;
        ";

        var items = new List<InspectionRecord>();

        await using (var queryCmd = connection.CreateCommand())
        {
            queryCmd.CommandText = querySql;
            foreach (var p in parameters) queryCmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
            queryCmd.Parameters.AddWithValue("@Limit", filter.PageSize);
            queryCmd.Parameters.AddWithValue("@Offset", offset);

            await using var reader = await queryCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = Guid.Parse(reader.GetString(0));
                var name = reader.GetString(1);
                var createdAt = DateTime.Parse(reader.GetString(2), null, DateTimeStyles.RoundtripKind);
                var updatedAt = DateTime.Parse(reader.GetString(3), null, DateTimeStyles.RoundtripKind);
                var mode = (DetectionMode)reader.GetInt32(4);
                var imagePath = reader.GetString(5);

                items.Add(new InspectionRecord(id, name, createdAt, updatedAt, mode, imagePath));
            }
        }

        return new PagedResult<InspectionRecord>(items, filter.PageNumber, filter.PageSize, totalItems);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InspectionRecord>> GetByDateRangeAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        const string sql = @"
            SELECT Id, Name, CreatedAtUtc, UpdatedAtUtc, DetectionMode, ImagePath
            FROM Inspections
            WHERE CreatedAtUtc >= @FromUtc AND CreatedAtUtc <= @ToUtc
            ORDER BY CreatedAtUtc DESC;
        ";

        var items = new List<InspectionRecord>();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@FromUtc", fromUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@ToUtc", toUtc.ToString("o"));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = Guid.Parse(reader.GetString(0));
            var name = reader.GetString(1);
            var createdAt = DateTime.Parse(reader.GetString(2), null, DateTimeStyles.RoundtripKind);
            var updatedAt = DateTime.Parse(reader.GetString(3), null, DateTimeStyles.RoundtripKind);
            var mode = (DetectionMode)reader.GetInt32(4);
            var imagePath = reader.GetString(5);

            items.Add(new InspectionRecord(id, name, createdAt, updatedAt, mode, imagePath));
        }

        return items;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateNameAsync(Guid id, string newName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        const string updateSql = @"
            UPDATE Inspections
            SET Name = @Name, UpdatedAtUtc = @UpdatedAtUtc
            WHERE Id = @Id;
        ";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = updateSql;
        cmd.Parameters.AddWithValue("@Id", id.ToString("D"));
        cmd.Parameters.AddWithValue("@Name", newName.Trim());
        cmd.Parameters.AddWithValue("@UpdatedAtUtc", DateTime.UtcNow.ToString("o"));

        int rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return rows > 0;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        const string deleteSql = "DELETE FROM Inspections WHERE Id = @Id;";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = deleteSql;
        cmd.Parameters.AddWithValue("@Id", id.ToString("D"));

        int rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return rows > 0;
    }

    /// <inheritdoc />
    public async Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM Inspections;";

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    private static async Task<IReadOnlyList<DetectedDefect>> LoadDefectsInternalAsync(SqliteConnection connection, Guid inspectionId, CancellationToken cancellationToken)
    {
        const string selectDefectsSql = @"
            SELECT Id, ClassCode, Confidence, BoxX, BoxY, BoxWidth, BoxHeight
            FROM Defects
            WHERE InspectionId = @InspectionId;
        ";

        var defects = new List<DetectedDefect>();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = selectDefectsSql;
        cmd.Parameters.AddWithValue("@InspectionId", inspectionId.ToString("D"));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var defectId = Guid.Parse(reader.GetString(0));
            var classCode = reader.GetString(1);
            var confidence = (float)reader.GetDouble(2);
            var boxX = (float)reader.GetDouble(3);
            var boxY = (float)reader.GetDouble(4);
            var boxW = (float)reader.GetDouble(5);
            var boxH = (float)reader.GetDouble(6);

            var box = new BoundingBox(boxX, boxY, boxW, boxH);
            defects.Add(new DetectedDefect(defectId, inspectionId, classCode, confidence, box));
        }

        return defects;
    }
}
