using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Persistence;
using pcb_deftek.Infrastructure.Persistence.Migrations;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using Reqnroll;

namespace pcb_deftek.Bdd.StepDefinitions;

[Binding]
public class InspectionPersistenceStepDefinitions : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteInspectionRepository _inspectionRepo;
    private readonly SqliteDefectRepository _defectRepo;

    private InspectionRecord? _currentRecord;
    private InspectionRecord? _retrievedRecord;
    private Guid _savedInspectionId;

    public InspectionPersistenceStepDefinitions()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"pcb-deftek-bdd-{Guid.NewGuid():N}.db");
        _connectionFactory = new SqliteConnectionFactory(_tempDbPath);

        // Run migrations for fresh test database
        var migrator = new SqliteDatabaseMigrator(_connectionFactory);
        migrator.MigrateToLatestAsync().GetAwaiter().GetResult();

        _inspectionRepo = new SqliteInspectionRepository(_connectionFactory);
        _defectRepo = new SqliteDefectRepository(_connectionFactory);
    }

    [Given(@"a configured SQLite persistence layer for testing")]
    public void GivenAConfiguredSqlitePersistenceLayerForTesting()
    {
        _connectionFactory.Should().NotBeNull();
        File.Exists(_tempDbPath).Should().BeTrue();
    }

    [Given(@"an inspection session for board ""(.*)"" with a ""(.*)"" defect")]
    public void GivenAnInspectionSessionForBoardWithADefect(string boardName, string defectCode)
    {
        _savedInspectionId = Guid.NewGuid();
        var box = new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f);
        var defect = new DetectedDefect(Guid.NewGuid(), _savedInspectionId, defectCode, 0.95f, box);

        _currentRecord = new InspectionRecord(
            id: _savedInspectionId,
            name: boardName,
            createdAtUtc: DateTime.UtcNow,
            updatedAtUtc: DateTime.UtcNow,
            mode: DetectionMode.SingleFile,
            imagePath: $"images/{boardName}.png",
            defects: new[] { defect });
    }

    [Given(@"an existing inspection for board ""(.*)"" with an ""(.*)"" defect")]
    public async Task GivenAnExistingInspectionForBoardWithADefect(string boardName, string defectCode)
    {
        GivenAnInspectionSessionForBoardWithADefect(boardName, defectCode);
        await WhenTheInspectionRecordIsSavedToTheDatabase();
    }

    [When(@"the inspection record is saved to the database")]
    public async Task WhenTheInspectionRecordIsSavedToTheDatabase()
    {
        _currentRecord.Should().NotBeNull();
        _savedInspectionId = await _inspectionRepo.InsertAsync(_currentRecord!);
    }

    [Then(@"the inspection record should be retrievable by its ID")]
    public async Task ThenTheInspectionRecordShouldBeRetrievableByItsId()
    {
        _retrievedRecord = await _inspectionRepo.GetByIdAsync(_savedInspectionId);
        _retrievedRecord.Should().NotBeNull();
        _retrievedRecord!.Id.Should().Be(_savedInspectionId);
    }

    [Then(@"the retrieved inspection should have (\d+) defect of class ""(.*)""")]
    public void ThenTheRetrievedInspectionShouldHaveDefectOfClass(int count, string defectCode)
    {
        _retrievedRecord.Should().NotBeNull();
        _retrievedRecord!.Defects.Should().HaveCount(count);
        _retrievedRecord.Defects[0].ClassCode.Should().Be(defectCode);
        _retrievedRecord.Defects[0].ClassName.Should().NotBeNullOrWhiteSpace();
    }

    [Then(@"the inspection status should evaluate to Fail")]
    public void ThenTheInspectionStatusShouldEvaluateToFail()
    {
        _retrievedRecord.Should().NotBeNull();
        _retrievedRecord!.Status.Should().Be(InspectionStatus.Fail);
    }

    [When(@"the inspection record is deleted from the database")]
    public async Task WhenTheInspectionRecordIsDeletedFromTheDatabase()
    {
        bool deleted = await _inspectionRepo.DeleteAsync(_savedInspectionId);
        deleted.Should().BeTrue();
    }

    [Then(@"the inspection should no longer exist in the database")]
    public async Task ThenTheInspectionShouldNoLongerExistInTheDatabase()
    {
        var fetched = await _inspectionRepo.GetByIdAsync(_savedInspectionId);
        fetched.Should().BeNull();
    }

    [Then(@"all associated defects should be removed automatically")]
    public async Task ThenAllAssociatedDefectsShouldBeRemovedAutomatically()
    {
        var defects = await _defectRepo.GetByInspectionIdAsync(_savedInspectionId);
        defects.Should().BeEmpty();
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
