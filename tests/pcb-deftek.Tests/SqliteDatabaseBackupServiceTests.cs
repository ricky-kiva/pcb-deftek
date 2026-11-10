using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Persistence;
using pcb_deftek.Infrastructure.Persistence.DatabaseBackup;
using pcb_deftek.Infrastructure.Persistence.Migrations;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using Xunit;

namespace pcb_deftek.Tests;

public class SqliteDatabaseBackupServiceTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly string _tempBackupDir;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteDatabaseMigrator _migrator;
    private readonly SqliteDatabaseBackupService _backupService;
    private readonly SqliteInspectionRepository _inspectionRepo;

    public SqliteDatabaseBackupServiceTests()
    {
        string uniqueId = Guid.NewGuid().ToString("N");
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"pcb-deftek-bkptest-{uniqueId}.db");
        _tempBackupDir = Path.Combine(Path.GetTempPath(), $"pcb-deftek-bkpdir-{uniqueId}");

        _factory = new SqliteConnectionFactory(_tempDbPath);
        _migrator = new SqliteDatabaseMigrator(_factory);
        _migrator.MigrateToLatestAsync().GetAwaiter().GetResult();

        _backupService = new SqliteDatabaseBackupService(_factory, _tempBackupDir);
        _inspectionRepo = new SqliteInspectionRepository(_factory);
    }

    [Fact]
    public void BackupDirectory_WhenDefault_ResolvesUnderDatabaseDirectory()
    {
        // Arrange
        var defaultService = new SqliteDatabaseBackupService(_factory);

        // Assert
        defaultService.BackupDirectory.Should().NotBeNullOrWhiteSpace();
        defaultService.BackupDirectory.Should().EndWith("backups");
    }

    [Fact]
    public async Task CreateBackupAsync_WhenCalled_CreatesHotSnapshotAndVerifiesIntegrity()
    {
        // Arrange: Insert data into active database
        var record = InspectionRecord.Create("Board-To-Backup", DetectionMode.SingleFile, "img.png");
        await _inspectionRepo.InsertAsync(record);

        // Act
        var result = await _backupService.CreateBackupAsync();

        // Assert
        result.Success.Should().BeTrue();
        result.BackupFilePath.Should().NotBeNullOrWhiteSpace();
        File.Exists(result.BackupFilePath).Should().BeTrue();
        result.FileSizeBytes.Should().BeGreaterThan(0);

        // Verify backup database content directly
        var backupFactory = new SqliteConnectionFactory(result.BackupFilePath);
        var backupRepo = new SqliteInspectionRepository(backupFactory);
        var restored = await backupRepo.GetByIdAsync(record.Id);
        restored.Should().NotBeNull();
        restored!.Name.Should().Be("Board-To-Backup");
    }

    [Fact]
    public async Task CreateBackupAsync_WithCustomDestinationPath_WritesToSpecifiedLocation()
    {
        // Arrange
        string customPath = Path.Combine(_tempBackupDir, "custom-snapshot.db");

        // Act
        var result = await _backupService.CreateBackupAsync(customPath);

        // Assert
        result.Success.Should().BeTrue();
        result.BackupFilePath.Should().Be(customPath);
        File.Exists(customPath).Should().BeTrue();
    }

    [Fact]
    public async Task RestoreBackupAsync_WhenCalled_RestoresDatabaseFromBackupSnapshot()
    {
        // Arrange: Insert data into database
        var original = InspectionRecord.Create("PersistentBoard", DetectionMode.LiveCamera, "cam.png");
        await _inspectionRepo.InsertAsync(original);

        // Create backup
        var backupResult = await _backupService.CreateBackupAsync();

        // Mutate/delete from active database
        await _inspectionRepo.DeleteAsync(original.Id);
        var afterDelete = await _inspectionRepo.GetByIdAsync(original.Id);
        afterDelete.Should().BeNull();

        // Act: Restore database from backup
        bool restored = await _backupService.RestoreBackupAsync(backupResult.BackupFilePath);

        // Assert
        restored.Should().BeTrue();
        var afterRestore = await _inspectionRepo.GetByIdAsync(original.Id);
        afterRestore.Should().NotBeNull();
        afterRestore!.Name.Should().Be("PersistentBoard");
    }

    [Fact]
    public async Task RestoreBackupAsync_WithNonExistentFile_ThrowsFileNotFoundException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _backupService.RestoreBackupAsync(Path.Combine(_tempBackupDir, "non_existent.db")));
    }

    [Fact]
    public async Task GetBackupsAsync_ReturnsExistingBackupsOrderedByCreationTimeDescending()
    {
        // Arrange
        var b1 = await _backupService.CreateBackupAsync();
        await Task.Delay(20);
        var b2 = await _backupService.CreateBackupAsync();

        // Act
        var backups = await _backupService.GetBackupsAsync();

        // Assert
        backups.Count.Should().BeGreaterThanOrEqualTo(2);
        backups[0].FilePath.Should().Be(b2.BackupFilePath);
        backups[1].FilePath.Should().Be(b1.BackupFilePath);
    }

    [Fact]
    public async Task PruneBackupsAsync_RemovesBackupsOlderThanRetentionPeriod()
    {
        // Arrange: Create backup file and backdate its creation timestamp
        var backup = await _backupService.CreateBackupAsync();
        File.SetCreationTimeUtc(backup.BackupFilePath, DateTime.UtcNow.AddDays(-40));

        // Act: Prune backups older than 30 days
        int prunedCount = await _backupService.PruneBackupsAsync(TimeSpan.FromDays(30));

        // Assert
        prunedCount.Should().Be(1);
        File.Exists(backup.BackupFilePath).Should().BeFalse();
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
        if (Directory.Exists(_tempBackupDir))
        {
            try { Directory.Delete(_tempBackupDir, recursive: true); } catch { /* ignore temp lock */ }
        }
    }
}
