using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Inference;
using pcb_deftek.Infrastructure.Persistence;
using pcb_deftek.Infrastructure.Persistence.Migrations;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Vision;
using Xunit;

namespace pcb_deftek.Tests;

public class BatchInspectionEngineTests : IDisposable
{
    private readonly Mock<IPcbDefectDetector> _mockDetector;
    private readonly Mock<IInspectionRepository> _mockRepository;
    private readonly ILogger<BatchInspectionEngine> _logger;
    private readonly string _tempDbDir;
    private readonly string _tempDbPath;

    public BatchInspectionEngineTests()
    {
        _mockDetector = new Mock<IPcbDefectDetector>();
        _mockRepository = new Mock<IInspectionRepository>();
        _logger = NullLogger<BatchInspectionEngine>.Instance;

        _tempDbDir = Path.Combine(Path.GetTempPath(), "pcb-deftek-batch-tests-" + Guid.NewGuid().ToString("N"));
        _tempDbPath = Path.Combine(_tempDbDir, "test.db");
        Directory.CreateDirectory(_tempDbDir);
    }

    [Fact]
    public async Task ExecuteBatchAsync_WhenPathsEmpty_ReturnsCompletedSummaryImmediately()
    {
        // Arrange
        var sut = new BatchInspectionEngine(_mockDetector.Object, _mockRepository.Object, _logger);

        // Act
        var result = await sut.ExecuteBatchAsync(Array.Empty<string>());

        // Assert
        result.Status.Should().Be(BatchStatus.Completed);
        result.TotalCount.Should().Be(0);
        result.ProcessedCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteBatchAsync_SingleFile_SetsSingleFileDetectionModeAndPersists()
    {
        // Arrange
        var filePath = @"C:\pcb\board1.jpg";
        var inspectionId = Guid.NewGuid();
        var mockResult = CreateInferenceResult(inspectionId, pass: true);

        _mockDetector
            .Setup(d => d.DetectFromFileAsync(filePath, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResult);

        InspectionRecord? savedRecord = null;
        _mockRepository
            .Setup(r => r.InsertAsync(It.IsAny<InspectionRecord>(), It.IsAny<CancellationToken>()))
            .Callback<InspectionRecord, CancellationToken>((rec, _) => savedRecord = rec)
            .ReturnsAsync(inspectionId);

        var sut = new BatchInspectionEngine(_mockDetector.Object, _mockRepository.Object, _logger);

        // Act
        var summary = await sut.ExecuteBatchAsync(new[] { filePath });

        // Assert
        summary.Status.Should().Be(BatchStatus.Completed);
        summary.TotalCount.Should().Be(1);
        summary.ProcessedCount.Should().Be(1);
        summary.PassCount.Should().Be(1);
        summary.FailCount.Should().Be(0);
        summary.Items.Should().HaveCount(1);
        summary.Items[0].Status.Should().Be(BatchItemStatus.Pass);

        savedRecord.Should().NotBeNull();
        savedRecord!.Mode.Should().Be(DetectionMode.SingleFile);
        savedRecord.Name.Should().Be("board1");
        savedRecord.ImagePath.Should().Be(filePath);
    }

    [Fact]
    public async Task ExecuteBatchAsync_MultiFile_SetsBatchFolderDetectionModeAndCalculatesEta()
    {
        // Arrange
        var files = new[] { @"C:\pcb\b1.jpg", @"C:\pcb\b2.jpg", @"C:\pcb\b3.jpg" };
        var reports = new List<BatchProgressReport>();
        var progress = new Progress<BatchProgressReport>(r => reports.Add(r));

        _mockDetector
            .Setup(d => d.DetectFromFileAsync(It.IsAny<string>(), null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, Guid? id, float? _, float? _, CancellationToken _) =>
            {
                Thread.Sleep(20);
                return CreateInferenceResult(id ?? Guid.NewGuid(), pass: path.EndsWith("b1.jpg"));
            });

        _mockRepository
            .Setup(r => r.InsertAsync(It.IsAny<InspectionRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var sut = new BatchInspectionEngine(_mockDetector.Object, _mockRepository.Object, _logger);

        // Act
        var summary = await sut.ExecuteBatchAsync(files, progress);

        // Assert
        summary.Status.Should().Be(BatchStatus.Completed);
        summary.TotalCount.Should().Be(3);
        summary.ProcessedCount.Should().Be(3);
        summary.PassCount.Should().Be(1);
        summary.FailCount.Should().Be(2);

        // At least one progress report after item 2 should have estimated time remaining computed
        reports.Should().NotBeEmpty();
        var laterReports = reports.Where(r => r.ProcessedCount >= 2).ToList();
        laterReports.Should().NotBeEmpty();
        laterReports[0].EstimatedTimeRemaining.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteBatchAsync_IndividualItemFails_ContinuesProcessingRemainingItems()
    {
        // Arrange
        var files = new[] { @"C:\pcb\good1.jpg", @"C:\pcb\corrupted.jpg", @"C:\pcb\good2.jpg" };

        _mockDetector
            .Setup(d => d.DetectFromFileAsync(files[0], null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateInferenceResult(Guid.NewGuid(), pass: true));

        _mockDetector
            .Setup(d => d.DetectFromFileAsync(files[1], null, null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Corrupt image header"));

        _mockDetector
            .Setup(d => d.DetectFromFileAsync(files[2], null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateInferenceResult(Guid.NewGuid(), pass: false));

        var sut = new BatchInspectionEngine(_mockDetector.Object, _mockRepository.Object, _logger);

        // Act
        var summary = await sut.ExecuteBatchAsync(files);

        // Assert
        summary.Status.Should().Be(BatchStatus.Completed);
        summary.TotalCount.Should().Be(3);
        summary.ProcessedCount.Should().Be(3);
        summary.PassCount.Should().Be(1);
        summary.FailCount.Should().Be(1);
        summary.ErrorCount.Should().Be(1);

        summary.Items[1].Status.Should().Be(BatchItemStatus.Error);
        summary.Items[1].ErrorMessage.Should().Contain("Corrupt image header");
        summary.Items[2].Status.Should().Be(BatchItemStatus.Fail);
    }

    [Fact]
    public async Task ExecuteBatchAsync_WhenCancelledMidBatch_HaltsGracefullyAndPreservesProcessedItems()
    {
        // Arrange
        var files = new[] { @"C:\pcb\b1.jpg", @"C:\pcb\b2.jpg", @"C:\pcb\b3.jpg" };
        using var cts = new CancellationTokenSource();

        _mockDetector
            .Setup(d => d.DetectFromFileAsync(files[0], null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateInferenceResult(Guid.NewGuid(), pass: true));

        _mockDetector
            .Setup(d => d.DetectFromFileAsync(files[1], null, null, null, It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ReturnsAsync(CreateInferenceResult(Guid.NewGuid(), pass: true));

        var sut = new BatchInspectionEngine(_mockDetector.Object, _mockRepository.Object, _logger);

        // Act
        var summary = await sut.ExecuteBatchAsync(files, cancellationToken: cts.Token);

        // Assert
        summary.Status.Should().Be(BatchStatus.Cancelled);
        summary.ProcessedCount.Should().BeInRange(1, 2);
        summary.Items.Count.Should().BeInRange(1, 2);
    }

    [Fact]
    public async Task ExecuteBatchAsync_WhenCancelledBeforeStart_ReturnsCancelledSummaryImmediately()
    {
        // Arrange
        var files = new[] { @"C:\pcb\b1.jpg", @"C:\pcb\b2.jpg" };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = new BatchInspectionEngine(_mockDetector.Object, _mockRepository.Object, _logger);

        // Act
        var summary = await sut.ExecuteBatchAsync(files, cancellationToken: cts.Token);

        // Assert
        summary.Status.Should().Be(BatchStatus.Cancelled);
        summary.ProcessedCount.Should().Be(0);
        summary.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteBatchAsync_WithRealSqliteRepository_PersistsInspectionAndDefects()
    {
        // Arrange: Real SQLite persistence
        var factory = new TestSqliteConnectionFactory(_tempDbPath);
        var migrator = new SqliteDatabaseMigrator(factory);
        await migrator.MigrateToLatestAsync();

        var inspectionRepo = new SqliteInspectionRepository(factory);
        var filePath = Path.Combine(AppContext.BaseDirectory, "TestData", "Model", "SH-1.jpg");
        if (!File.Exists(filePath))
        {
            filePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestData", "Model", "SH-1.jpg"));
        }

        var mockResult = CreateInferenceResult(Guid.NewGuid(), pass: false);
        _mockDetector
            .Setup(d => d.DetectFromFileAsync(filePath, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResult);

        var sut = new BatchInspectionEngine(_mockDetector.Object, inspectionRepo, _logger);

        // Act
        var summary = await sut.ExecuteBatchAsync(new[] { filePath });

        // Assert
        summary.Status.Should().Be(BatchStatus.Completed);
        summary.ProcessedCount.Should().Be(1);
        summary.FailCount.Should().Be(1);

        var persistedRecord = await inspectionRepo.GetByIdAsync(summary.Items[0].InspectionId!.Value);
        persistedRecord.Should().NotBeNull();
        persistedRecord!.Name.Should().Be("SH-1");
        persistedRecord.Mode.Should().Be(DetectionMode.SingleFile);
        persistedRecord.Defects.Should().HaveCount(1);
        persistedRecord.Defects[0].ClassCode.Should().Be("short");
    }

    [Fact]
    public async Task ExecuteBatchAsync_MultipleRealImages_PersistsAllRecordsWithBatchFolderMode()
    {
        // Arrange
        var factory = new TestSqliteConnectionFactory(_tempDbPath);
        var migrator = new SqliteDatabaseMigrator(factory);
        await migrator.MigrateToLatestAsync();

        var inspectionRepo = new SqliteInspectionRepository(factory);
        var testDataDir = Path.Combine(AppContext.BaseDirectory, "TestData", "Model");
        if (!Directory.Exists(testDataDir))
        {
            testDataDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestData", "Model"));
        }

        var file1 = Path.Combine(testDataDir, "SH-1.jpg");
        var file2 = Path.Combine(testDataDir, "SP-1.jpg");

        _mockDetector
            .Setup(d => d.DetectFromFileAsync(file1, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateInferenceResult(Guid.NewGuid(), pass: false));

        _mockDetector
            .Setup(d => d.DetectFromFileAsync(file2, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateInferenceResult(Guid.NewGuid(), pass: true));

        var sut = new BatchInspectionEngine(_mockDetector.Object, inspectionRepo, _logger);

        // Act
        var summary = await sut.ExecuteBatchAsync(new[] { file1, file2 });

        // Assert
        summary.Status.Should().Be(BatchStatus.Completed);
        summary.ProcessedCount.Should().Be(2);
        summary.PassCount.Should().Be(1);
        summary.FailCount.Should().Be(1);
        summary.Items.Should().HaveCount(2);

        var rec1 = await inspectionRepo.GetByIdAsync(summary.Items[0].InspectionId!.Value);
        var rec2 = await inspectionRepo.GetByIdAsync(summary.Items[1].InspectionId!.Value);

        rec1.Should().NotBeNull();
        rec1!.Mode.Should().Be(DetectionMode.BatchFolder);
        rec1.Name.Should().Be("SH-1");
        rec1.Defects.Should().HaveCount(1);

        rec2.Should().NotBeNull();
        rec2!.Mode.Should().Be(DetectionMode.BatchFolder);
        rec2.Name.Should().Be("SP-1");
        rec2.Defects.Should().BeEmpty();
    }

    [Theory]
    [InlineData(10, 0, 0)]
    [InlineData(10, 5, 50)]
    [InlineData(10, 10, 100)]
    [InlineData(0, 0, 0)]
    public void BatchProgressReport_CalculatesPercentCompleteAccurately(int total, int processed, int expectedPercent)
    {
        // Arrange
        var report = new BatchProgressReport(
            TotalCount: total,
            ProcessedCount: processed,
            PassCount: processed,
            FailCount: 0,
            ErrorCount: 0,
            ElapsedTime: TimeSpan.FromSeconds(5),
            EstimatedTimeRemaining: TimeSpan.FromSeconds(5));

        // Assert
        report.PercentComplete.Should().Be(expectedPercent);
    }

    [Fact]
    public void BatchProgressReport_CalculatesSpeedAccurately()
    {
        // Arrange
        var report = new BatchProgressReport(
            TotalCount: 10,
            ProcessedCount: 4,
            PassCount: 4,
            FailCount: 0,
            ErrorCount: 0,
            ElapsedTime: TimeSpan.FromSeconds(2.0),
            EstimatedTimeRemaining: TimeSpan.FromSeconds(3.0));

        // Assert
        report.SpeedItemsPerSecond.Should().BeApproximately(2.0, 0.01);
    }

    private static InferenceResult CreateInferenceResult(Guid id, bool pass)
    {
        var defects = pass
            ? Array.Empty<DetectedDefect>()
            : new[]
            {
                new DetectedDefect(
                    id: Guid.NewGuid(),
                    inspectionId: id,
                    classCode: "short",
                    confidence: 0.88f,
                    boundingBox: new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f))
            };
        return new InferenceResult(
            InspectionId: id,
            Defects: defects,
            Transform: new LetterboxTransform(640, 640, 640, 640, 1.0f, 0, 0),
            Device: ExecutionDevice.Cpu,
            Timing: new InferenceTiming(5, 20, 2, 27));
    }

    public void Dispose()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDbDir))
            {
                Directory.Delete(_tempDbDir, true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private class TestSqliteConnectionFactory : ISqliteConnectionFactory
    {
        public string DatabasePath { get; }
        public TestSqliteConnectionFactory(string path) => DatabasePath = path;
        public SqliteConnection CreateConnection() => new($"Data Source={DatabasePath}");
        public async ValueTask<SqliteConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            return conn;
        }
    }
}
