using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Persistence;
using pcb_deftek.Infrastructure.Persistence.Migrations;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using Xunit;

namespace pcb_deftek.Tests;

public class SqliteRepositoryIntegrationTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteInspectionRepository _inspectionRepo;
    private readonly SqliteDefectRepository _defectRepo;
    private readonly SqliteSettingsRepository _settingsRepo;

    public SqliteRepositoryIntegrationTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"pcb-deftek-repo-{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(_tempDbPath);

        // Migrate to latest schema first
        var migrator = new SqliteDatabaseMigrator(_factory);
        migrator.MigrateToLatestAsync().GetAwaiter().GetResult();

        _inspectionRepo = new SqliteInspectionRepository(_factory);
        _defectRepo = new SqliteDefectRepository(_factory);
        _settingsRepo = new SqliteSettingsRepository(_factory);
    }

    [Fact]
    public async Task InspectionRepo_InsertAsync_PersistsInspectionAndReturnsId()
    {
        // Arrange
        var record = InspectionRecord.Create("Inspection-001", DetectionMode.SingleFile, "images/1.png");

        // Act
        var insertedId = await _inspectionRepo.InsertAsync(record);

        // Assert
        insertedId.Should().Be(record.Id);
    }

    [Fact]
    public async Task InspectionRepo_GetByIdAsync_WhenRecordExists_ReturnsFullyPopulatedAggregateWithDefects()
    {
        // Arrange
        var inspectionId = Guid.NewGuid();
        var defectId = Guid.NewGuid();
        var box = new BoundingBox(0.1f, 0.2f, 0.3f, 0.4f);
        var defect = new DetectedDefect(defectId, inspectionId, "short", 0.95f, box);

        var record = new InspectionRecord(
            id: inspectionId,
            name: "Board-Alpha",
            createdAtUtc: DateTime.UtcNow,
            updatedAtUtc: DateTime.UtcNow,
            mode: DetectionMode.LiveCamera,
            imagePath: "images/alpha.png",
            defects: new[] { defect });

        await _inspectionRepo.InsertAsync(record);

        // Act
        var fetched = await _inspectionRepo.GetByIdAsync(inspectionId);

        // Assert
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(inspectionId);
        fetched.Name.Should().Be("Board-Alpha");
        fetched.Mode.Should().Be(DetectionMode.LiveCamera);
        fetched.ImagePath.Should().Be("images/alpha.png");
        fetched.DefectCount.Should().Be(1);
        fetched.Status.Should().Be(InspectionStatus.Fail);
        fetched.Defects.Should().HaveCount(1);

        var fetchedDefect = fetched.Defects[0];
        fetchedDefect.Id.Should().Be(defectId);
        fetchedDefect.ClassCode.Should().Be("short");
        fetchedDefect.Confidence.Should().BeApproximately(0.95f, 0.001f);
        fetchedDefect.BoundingBox.X.Should().BeApproximately(0.1f, 0.001f);
        fetchedDefect.BoundingBox.Y.Should().BeApproximately(0.2f, 0.001f);
    }

    [Fact]
    public async Task InspectionRepo_GetByIdAsync_WhenRecordDoesNotExist_ReturnsNull()
    {
        // Act
        var result = await _inspectionRepo.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task InspectionRepo_GetPagedAsync_WithStatusPassFilter_ReturnsOnlyZeroDefectInspections()
    {
        // Arrange: Insert one Pass and one Fail inspection
        var passRecord = InspectionRecord.Create("PassBoard", DetectionMode.SingleFile, "images/pass.png");
        await _inspectionRepo.InsertAsync(passRecord);

        var failId = Guid.NewGuid();
        var defect = new DetectedDefect(Guid.NewGuid(), failId, "open", 0.9f, new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f));
        var failRecord = new InspectionRecord(failId, "FailBoard", DateTime.UtcNow, DateTime.UtcNow, DetectionMode.SingleFile, "images/fail.png", new[] { defect });
        await _inspectionRepo.InsertAsync(failRecord);

        var filter = new InspectionQueryFilter { Status = InspectionStatus.Pass, PageSize = 10 };

        // Act
        var paged = await _inspectionRepo.GetPagedAsync(filter);

        // Assert
        paged.TotalItems.Should().Be(1);
        paged.Items.Should().HaveCount(1);
        paged.Items[0].Id.Should().Be(passRecord.Id);
    }

    [Fact]
    public async Task InspectionRepo_GetPagedAsync_WithDateFilter_ReturnsOnlyInspectionsWithinWindow()
    {
        // Arrange
        var oldDate = DateTime.UtcNow.AddDays(-10);
        var recentDate = DateTime.UtcNow.AddDays(-1);

        var oldRecord = new InspectionRecord(Guid.NewGuid(), "OldBoard", oldDate, oldDate, DetectionMode.SingleFile, "old.png");
        var recentRecord = new InspectionRecord(Guid.NewGuid(), "RecentBoard", recentDate, recentDate, DetectionMode.SingleFile, "recent.png");

        await _inspectionRepo.InsertAsync(oldRecord);
        await _inspectionRepo.InsertAsync(recentRecord);

        var filter = new InspectionQueryFilter
        {
            StartDateUtc = DateTime.UtcNow.AddDays(-3),
            EndDateUtc = DateTime.UtcNow,
            PageSize = 10
        };

        // Act
        var paged = await _inspectionRepo.GetPagedAsync(filter);

        // Assert
        paged.TotalItems.Should().Be(1);
        paged.Items[0].Id.Should().Be(recentRecord.Id);
    }

    [Fact]
    public async Task InspectionRepo_GetByDateRangeAsync_ReturnsRecordsOrderedByCreatedAtUtcDescending()
    {
        // Arrange
        var t1 = DateTime.UtcNow.AddHours(-3);
        var t2 = DateTime.UtcNow.AddHours(-1);

        var r1 = new InspectionRecord(Guid.NewGuid(), "Earlier", t1, t1, DetectionMode.BatchFolder, "1.png");
        var r2 = new InspectionRecord(Guid.NewGuid(), "Later", t2, t2, DetectionMode.BatchFolder, "2.png");

        await _inspectionRepo.InsertAsync(r1);
        await _inspectionRepo.InsertAsync(r2);

        // Act
        var results = await _inspectionRepo.GetByDateRangeAsync(DateTime.UtcNow.AddHours(-5), DateTime.UtcNow);

        // Assert
        results.Should().HaveCount(2);
        results[0].Id.Should().Be(r2.Id);
        results[1].Id.Should().Be(r1.Id);
    }

    [Fact]
    public async Task InspectionRepo_UpdateNameAsync_WhenRecordExists_UpdatesNameAndReturnsTrue()
    {
        // Arrange
        var record = InspectionRecord.Create("OriginalName", DetectionMode.LiveCamera, "cam.png");
        await _inspectionRepo.InsertAsync(record);

        // Act
        bool updated = await _inspectionRepo.UpdateNameAsync(record.Id, "RenamedInspection");
        var fetched = await _inspectionRepo.GetByIdAsync(record.Id);

        // Assert
        updated.Should().BeTrue();
        fetched!.Name.Should().Be("RenamedInspection");
    }

    [Fact]
    public async Task InspectionRepo_DeleteAsync_WhenRecordExists_RemovesRecordAndCascadesToDefects()
    {
        // Arrange
        var inspectionId = Guid.NewGuid();
        var defect = new DetectedDefect(Guid.NewGuid(), inspectionId, "spur", 0.9f, new BoundingBox(0.2f, 0.2f, 0.1f, 0.1f));
        var record = new InspectionRecord(inspectionId, "DeleteMe", DateTime.UtcNow, DateTime.UtcNow, DetectionMode.SingleFile, "del.png", new[] { defect });
        await _inspectionRepo.InsertAsync(record);

        // Act
        bool deleted = await _inspectionRepo.DeleteAsync(inspectionId);
        var fetchedInspection = await _inspectionRepo.GetByIdAsync(inspectionId);
        var remainingDefects = await _defectRepo.GetByInspectionIdAsync(inspectionId);

        // Assert
        deleted.Should().BeTrue();
        fetchedInspection.Should().BeNull();
        remainingDefects.Should().BeEmpty("Cascading foreign key delete must remove associated defects");
    }

    [Fact]
    public async Task DefectRepo_InsertBatchAsync_PersistsDefectsAndUpdateParentDefectCount()
    {
        // Arrange
        var inspectionId = Guid.NewGuid();
        var inspection = InspectionRecord.Create("InspectionWithoutDefects", DetectionMode.SingleFile, "img.png", id: inspectionId);
        await _inspectionRepo.InsertAsync(inspection);

        var d1 = new DetectedDefect(Guid.NewGuid(), inspectionId, "short", 0.92f, new BoundingBox(0.1f, 0.1f, 0.1f, 0.1f));
        var d2 = new DetectedDefect(Guid.NewGuid(), inspectionId, "open", 0.88f, new BoundingBox(0.2f, 0.2f, 0.1f, 0.1f));

        // Act
        await _defectRepo.InsertBatchAsync(inspectionId, new[] { d1, d2 });
        var defects = await _defectRepo.GetByInspectionIdAsync(inspectionId);
        var updatedInspection = await _inspectionRepo.GetByIdAsync(inspectionId);

        // Assert
        defects.Should().HaveCount(2);
        updatedInspection!.DefectCount.Should().Be(2);
        updatedInspection.Status.Should().Be(InspectionStatus.Fail);
    }

    [Fact]
    public async Task DefectRepo_GetClassDistributionAsync_ComputesAccurateDefectCountsPerClassCode()
    {
        // Arrange
        var i1 = Guid.NewGuid();
        var i2 = Guid.NewGuid();
        await _inspectionRepo.InsertAsync(InspectionRecord.Create("I1", DetectionMode.SingleFile, "1.png", id: i1));
        await _inspectionRepo.InsertAsync(InspectionRecord.Create("I2", DetectionMode.SingleFile, "2.png", id: i2));

        var d1 = new DetectedDefect(Guid.NewGuid(), i1, "short", 0.9f, new BoundingBox(0.1f, 0.1f, 0.1f, 0.1f));
        var d2 = new DetectedDefect(Guid.NewGuid(), i1, "short", 0.95f, new BoundingBox(0.2f, 0.2f, 0.1f, 0.1f));
        var d3 = new DetectedDefect(Guid.NewGuid(), i2, "open", 0.85f, new BoundingBox(0.3f, 0.3f, 0.1f, 0.1f));

        await _defectRepo.InsertBatchAsync(i1, new[] { d1, d2 });
        await _defectRepo.InsertBatchAsync(i2, new[] { d3 });

        // Act
        var distribution = await _defectRepo.GetClassDistributionAsync();
        int totalDefects = await _defectRepo.GetTotalDefectCountAsync();

        // Assert
        distribution.Should().ContainKey("short").WhoseValue.Should().Be(2);
        distribution.Should().ContainKey("open").WhoseValue.Should().Be(1);
        totalDefects.Should().Be(3);
    }

    [Fact]
    public async Task SettingsRepo_SetAndGetSettingAsync_WithPrimitiveType_RoundTripsCorrectly()
    {
        // Act
        await _settingsRepo.SetSettingAsync("ConfidenceThreshold", 0.75);
        double threshold = await _settingsRepo.GetSettingAsync<double>("ConfidenceThreshold");

        // Assert
        threshold.Should().Be(0.75);
    }

    public record TestUserSettings(string DefaultStartupPage, bool EnableKoFi, List<string> EnabledClasses);

    [Fact]
    public async Task SettingsRepo_SetAndGetSettingAsync_WithComplexObject_SerializesAndDeserializesJson()
    {
        // Arrange
        var original = new TestUserSettings("Camera", true, new List<string> { "short", "open" });

        // Act
        await _settingsRepo.SetSettingAsync("UserSettings", original);
        var retrieved = await _settingsRepo.GetSettingAsync<TestUserSettings>("UserSettings");

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.DefaultStartupPage.Should().Be("Camera");
        retrieved.EnableKoFi.Should().BeTrue();
        retrieved.EnabledClasses.Should().BeEquivalentTo(new[] { "short", "open" });
    }

    [Fact]
    public async Task SettingsRepo_SetSettingAsync_WhenKeyExists_UpsertsValue()
    {
        // Arrange
        await _settingsRepo.SetSettingAsync("Theme", "Light");

        // Act
        await _settingsRepo.SetSettingAsync("Theme", "Dark");
        string? theme = await _settingsRepo.GetSettingAsync<string>("Theme");

        // Assert
        theme.Should().Be("Dark");
    }

    [Fact]
    public async Task SettingsRepo_GetSettingAsync_WhenKeyDoesNotExist_ReturnsDefault()
    {
        // Act
        string? nonExistent = await _settingsRepo.GetSettingAsync<string>("NonExistentKey");

        // Assert
        nonExistent.Should().BeNull();
    }

    [Fact]
    public async Task SettingsRepo_GetAllSettingsAsync_ReturnsAllStoredKeyValuePairs()
    {
        // Arrange
        await _settingsRepo.SetSettingAsync("KeyA", "ValA");
        await _settingsRepo.SetSettingAsync("KeyB", "ValB");

        // Act
        var all = await _settingsRepo.GetAllSettingsAsync();

        // Assert
        all.Should().ContainKey("KeyA").WhoseValue.Should().Be("ValA");
        all.Should().ContainKey("KeyB").WhoseValue.Should().Be("ValB");
    }

    [Fact]
    public async Task SettingsRepo_DeleteSettingAsync_RemovesKeyAndReturnsTrue()
    {
        // Arrange
        await _settingsRepo.SetSettingAsync("TempKey", "TempValue");

        // Act
        bool deleted = await _settingsRepo.DeleteSettingAsync("TempKey");
        string? result = await _settingsRepo.GetSettingAsync<string>("TempKey");

        // Assert
        deleted.Should().BeTrue();
        result.Should().BeNull();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { /* ignore lock */ }
        }
        string walFile = _tempDbPath + "-wal";
        if (File.Exists(walFile))
        {
            try { File.Delete(walFile); } catch { /* ignore lock */ }
        }
        string shmFile = _tempDbPath + "-shm";
        if (File.Exists(shmFile))
        {
            try { File.Delete(shmFile); } catch { /* ignore lock */ }
        }
    }
}
