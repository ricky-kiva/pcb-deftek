using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Infrastructure.Persistence;
using pcb_deftek.Infrastructure.Persistence.DatabaseExport;
using Xunit;

namespace pcb_deftek.Tests;

public class SqliteDatabaseExportServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _dbPath;
    private readonly string _defaultExportDir;
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteDatabaseExportServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "pcb-deftek-export-tests-" + Guid.NewGuid().ToString("N"));
        _dbPath = Path.Combine(_testRoot, "test-app.db");
        _defaultExportDir = Path.Combine(_testRoot, "default-exports");
        Directory.CreateDirectory(_testRoot);
        Directory.CreateDirectory(_defaultExportDir);

        _connectionFactory = new TestConnectionFactory(_dbPath);

        // Seed some sample data in source database
        using var conn = _connectionFactory.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE TestTable (Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO TestTable (Name) VALUES ('Item1'), ('Item2');";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup in test temp
        }
    }

    [Fact]
    public async Task ExportDatabaseAsync_WithCustomDirectory_CreatesValidSqliteDatabaseFile()
    {
        // Arrange
        var customExportDir = Path.Combine(_testRoot, "custom-exports");
        var service = new SqliteDatabaseExportService(_connectionFactory, _defaultExportDir);

        // Act
        string exportedPath = await service.ExportDatabaseAsync(customExportDir);

        // Assert
        File.Exists(exportedPath).Should().BeTrue();
        Path.GetDirectoryName(exportedPath).Should().Be(Path.GetFullPath(customExportDir));
        Path.GetFileName(exportedPath).Should().StartWith("pcb-deftek-export-");
        Path.GetExtension(exportedPath).Should().Be(".db");

        // Verify exported database contents
        using var exportedConn = new SqliteConnection($"Data Source={exportedPath};Mode=ReadOnly");
        exportedConn.Open();
        using var verifyCmd = exportedConn.CreateCommand();
        verifyCmd.CommandText = "SELECT COUNT(*) FROM TestTable;";
        var count = Convert.ToInt32(verifyCmd.ExecuteScalar());
        count.Should().Be(2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExportDatabaseAsync_WithNullOrWhitespaceDirectory_DefaultsToConfiguredDownloadsDirectory(string? destinationDir)
    {
        // Arrange
        var service = new SqliteDatabaseExportService(_connectionFactory, _defaultExportDir);

        // Act
        string exportedPath = await service.ExportDatabaseAsync(destinationDir);

        // Assert
        File.Exists(exportedPath).Should().BeTrue();
        Path.GetDirectoryName(exportedPath).Should().Be(Path.GetFullPath(_defaultExportDir));
    }

    [Fact]
    public async Task ExportDatabaseAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var service = new SqliteDatabaseExportService(_connectionFactory, _defaultExportDir);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExportDatabaseAsync(_defaultExportDir, cts.Token));
    }

    [Fact]
    public async Task ExportDatabaseAsync_WhenTargetDirectoryInvalid_ThrowsExportException()
    {
        // Arrange
        var service = new SqliteDatabaseExportService(_connectionFactory, _defaultExportDir);
        // An invalid path containing illegal characters or path traversal errors on Windows
        string invalidPath = "Z:\\NonExistentDriveDirectory\\SubDir\\";

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ExportException>(() =>
            service.ExportDatabaseAsync(invalidPath));

        ex.Should().NotBeNull();
        ex.DestinationPath.Should().NotBeNull();
    }

    private sealed class TestConnectionFactory : ISqliteConnectionFactory
    {
        public string DatabasePath { get; }

        public TestConnectionFactory(string dbPath)
        {
            DatabasePath = dbPath;
        }

        public SqliteConnection CreateConnection() => new($"Data Source={DatabasePath}");

        public async ValueTask<SqliteConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            return conn;
        }
    }
}
