using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Infrastructure.Persistence;
using pcb_deftek.Infrastructure.Persistence.Migrations;
using Xunit;

namespace pcb_deftek.Tests;

public class SqliteMigrationTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteDatabaseMigrator _migrator;

    public SqliteMigrationTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"pcb-deftek-migtest-{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(_tempDbPath);
        _migrator = new SqliteDatabaseMigrator(_factory);
    }

    [Fact]
    public async Task CreateOpenConnectionAsync_WhenCalled_EnforcesForeignKeyPragma()
    {
        // Arrange & Act
        await using var connection = await _factory.CreateOpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys;";
        var result = await cmd.ExecuteScalarAsync();

        // Assert
        Convert.ToInt32(result).Should().Be(1, "Foreign keys must be actively enabled in SQLite");
    }

    [Fact]
    public async Task CreateOpenConnectionAsync_WhenCalled_EnforcesWalModePragma()
    {
        // Arrange & Act
        await using var connection = await _factory.CreateOpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        var result = await cmd.ExecuteScalarAsync();

        // Assert
        result?.ToString().Should().BeEquivalentTo("wal", "Write-Ahead Logging must be active for concurrency");
    }

    [Fact]
    public async Task MigrateToLatestAsync_OnFreshDatabase_AppliesInitialSchemaSuccessfully()
    {
        // Act
        var result = await _migrator.MigrateToLatestAsync();

        // Assert
        result.Success.Should().BeTrue();
        result.PreviousVersion.Should().Be(0);
        result.CurrentVersion.Should().Be(2);
        result.MigrationsApplied.Should().Be(2);

        // Verify tables exist
        await using var connection = await _factory.CreateOpenConnectionAsync();
        await VerifyTableExistsAsync(connection, "SchemaMigrations");
        await VerifyTableExistsAsync(connection, "Inspections");
        await VerifyTableExistsAsync(connection, "Defects");
        await VerifyTableExistsAsync(connection, "AppSettings");
    }

    [Fact]
    public async Task MigrateToLatestAsync_WhenRunRepeatedly_IsIdempotent()
    {
        // Arrange
        await _migrator.MigrateToLatestAsync();

        // Act
        var secondRunResult = await _migrator.MigrateToLatestAsync();

        // Assert
        secondRunResult.Success.Should().BeTrue();
        secondRunResult.PreviousVersion.Should().Be(2);
        secondRunResult.CurrentVersion.Should().Be(2);
        secondRunResult.MigrationsApplied.Should().Be(0);
    }

    [Fact]
    public async Task GetMigrationHistoryAsync_AfterMigration_ReturnsAuditHistoryWithSha256Checksum()
    {
        // Arrange
        await _migrator.MigrateToLatestAsync();

        // Act
        var history = await _migrator.GetMigrationHistoryAsync();

        // Assert
        history.Should().HaveCount(2);
        var entry = history[1];
        entry.Version.Should().Be(2);
        entry.Description.Should().Be("add_inspection_origin");
        entry.Checksum.Should().HaveLength(64, "SHA-256 hash in hex format has exactly 64 characters");
        entry.ExecutionDurationMs.Should().BeGreaterThanOrEqualTo(0);
        entry.AppliedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
        history[0].Version.Should().Be(1);
        history[0].Description.Should().Be("initial_schema");
    }

    [Fact]
    public async Task RollbackAsync_DownToVersionZero_RevertsAllTablesCleanly()
    {
        // Arrange
        await _migrator.MigrateToLatestAsync();

        // Act
        var rollbackResult = await _migrator.RollbackAsync(0);

        // Assert
        rollbackResult.Success.Should().BeTrue();
        rollbackResult.PreviousVersion.Should().Be(2);
        rollbackResult.CurrentVersion.Should().Be(0);
        rollbackResult.MigrationsApplied.Should().Be(2);

        // Verify tables no longer exist
        await using var connection = await _factory.CreateOpenConnectionAsync();
        await VerifyTableDoesNotExistAsync(connection, "Inspections");
        await VerifyTableDoesNotExistAsync(connection, "Defects");
        await VerifyTableDoesNotExistAsync(connection, "AppSettings");
    }

    [Fact]
    public async Task RollbackAsync_WithNegativeTargetVersion_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _migrator.RollbackAsync(-1));
    }

    [Fact]
    public async Task Schema_WhenInsertingDefectWithInvalidInspectionId_EnforcesForeignKeyViolation()
    {
        // Arrange
        await _migrator.MigrateToLatestAsync();
        await using var connection = await _factory.CreateOpenConnectionAsync();

        const string insertDefectSql = @"
            INSERT INTO Defects (Id, InspectionId, ClassCode, Confidence, BoxX, BoxY, BoxWidth, BoxHeight)
            VALUES (@Id, @InspectionId, @ClassCode, @Confidence, @BoxX, @BoxY, @BoxWidth, @BoxHeight);
        ";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = insertDefectSql;
        cmd.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString("D"));
        cmd.Parameters.AddWithValue("@InspectionId", Guid.NewGuid().ToString("D")); // non-existent FK
        cmd.Parameters.AddWithValue("@ClassCode", "short");
        cmd.Parameters.AddWithValue("@Confidence", 0.95);
        cmd.Parameters.AddWithValue("@BoxX", 0.1);
        cmd.Parameters.AddWithValue("@BoxY", 0.1);
        cmd.Parameters.AddWithValue("@BoxWidth", 0.2);
        cmd.Parameters.AddWithValue("@BoxHeight", 0.2);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
        ex.SqliteErrorCode.Should().Be(19); // 19 = SQLite constraint violation (FOREIGN KEY)
    }

    [Fact]
    public async Task Schema_WhenInsertingDefectWithOutOfRangeConfidence_EnforcesCheckConstraint()
    {
        // Arrange
        await _migrator.MigrateToLatestAsync();
        await using var connection = await _factory.CreateOpenConnectionAsync();

        // Insert valid inspection first
        string inspectionId = Guid.NewGuid().ToString("D");
        await using (var inspCmd = connection.CreateCommand())
        {
            inspCmd.CommandText = @"
                INSERT INTO Inspections (Id, Name, CreatedAtUtc, UpdatedAtUtc, DetectionMode, DefectCount, ImagePath)
                VALUES (@Id, 'TestInspection', @CreatedAt, @UpdatedAt, 0, 0, 'image.png');
            ";
            inspCmd.Parameters.AddWithValue("@Id", inspectionId);
            inspCmd.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow.ToString("o"));
            inspCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
            await inspCmd.ExecuteNonQueryAsync();
        }

        // Attempt defect with invalid confidence > 1.0
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Defects (Id, InspectionId, ClassCode, Confidence, BoxX, BoxY, BoxWidth, BoxHeight)
            VALUES (@Id, @InspectionId, 'short', 1.5, 0.1, 0.1, 0.2, 0.2);
        ";
        cmd.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString("D"));
        cmd.Parameters.AddWithValue("@InspectionId", inspectionId);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
        ex.SqliteErrorCode.Should().Be(19); // Constraint violation (CHECK)
    }

    private static async Task VerifyTableExistsAsync(SqliteConnection connection, string tableName)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = @TableName;";
        cmd.Parameters.AddWithValue("@TableName", tableName);
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        count.Should().Be(1, $"Table '{tableName}' should exist in the SQLite database");
    }

    private static async Task VerifyTableDoesNotExistAsync(SqliteConnection connection, string tableName)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = @TableName;";
        cmd.Parameters.AddWithValue("@TableName", tableName);
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        count.Should().Be(0, $"Table '{tableName}' should no longer exist after rollback");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { /* ignore temp lock */ }
        }
        string walFile = _tempDbPath + "-wal";
        if (File.Exists(walFile))
        {
            try { File.Delete(walFile); } catch { /* ignore temp lock */ }
        }
        string shmFile = _tempDbPath + "-shm";
        if (File.Exists(shmFile))
        {
            try { File.Delete(shmFile); } catch { /* ignore temp lock */ }
        }
    }
}
