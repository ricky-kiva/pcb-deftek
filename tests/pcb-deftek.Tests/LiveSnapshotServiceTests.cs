using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCvSharp;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Inference;
using pcb_deftek.Infrastructure.Persistence;
using pcb_deftek.Infrastructure.Persistence.Migrations;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;
using pcb_deftek.Infrastructure.Vision;
using Xunit;

namespace pcb_deftek.Tests;

public class LiveSnapshotServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempDbPath;
    private readonly string _capturesDir;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteInspectionRepository _repository;
    private readonly Mock<ILiveInspectionEngine> _mockEngine = new();

    public LiveSnapshotServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pcb-deftek-snapshot-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _tempDbPath = Path.Combine(_tempDir, "test.db");
        _capturesDir = Path.Combine(_tempDir, "Captures");

        _connectionFactory = new SqliteConnectionFactory(_tempDbPath);
        var migrator = new SqliteDatabaseMigrator(_connectionFactory);
        migrator.MigrateToLatestAsync().GetAwaiter().GetResult();

        _repository = new SqliteInspectionRepository(_connectionFactory);
    }

    private LiveSnapshotService CreateSut()
    {
        return new LiveSnapshotService(
            _repository,
            _mockEngine.Object,
            _capturesDir,
            NullLogger<LiveSnapshotService>.Instance);
    }

    private static LiveFrameResult CreateTestFrameResult(int defectCount = 2)
    {
        var rawMat = new Mat(120, 160, MatType.CV_8UC3, new Scalar(40, 80, 120));
        int stride = (int)rawMat.Step();
        var annotatedBitmap = BitmapSource.Create(
            rawMat.Width,
            rawMat.Height,
            96.0,
            96.0,
            PixelFormats.Bgr24,
            null,
            rawMat.Data,
            stride * rawMat.Height,
            stride);
        annotatedBitmap.Freeze();

        var defects = new DetectedDefect[defectCount];
        for (int i = 0; i < defectCount; i++)
        {
            defects[i] = new DetectedDefect(
                Guid.NewGuid(),
                Guid.NewGuid(),
                i % 2 == 0 ? "short" : "hole_breakout",
                0.92f,
                new BoundingBox(0.15f, 0.20f, 0.25f, 0.30f));
        }

        var inference = new InferenceResult(
            Guid.NewGuid(),
            defects,
            new LetterboxTransform(640, 640, 160, 120, 4.0f, 0, 80),
            ExecutionDevice.GpuDirectML,
            new InferenceTiming(2, 11, 1, 14));

        var telemetry = new LiveInspectionTelemetry(14.0, 14.0, 60.0, "DirectML GPU", DateTime.UtcNow);

        return new LiveFrameResult(annotatedBitmap, rawMat, inference, telemetry);
    }

    [Fact]
    public async Task CaptureSnapshotAsync_UsesConfiguredImageOutputDirectory()
    {
        var settings = new Mock<IUserSettingsService>();
        settings
            .Setup(service => service.LoadSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSettings { ImageOutputDirectory = _capturesDir });
        var sut = new LiveSnapshotService(
            _repository,
            _mockEngine.Object,
            capturesDirectoryPath: null,
            NullLogger<LiveSnapshotService>.Instance,
            settings.Object);
        using var frameResult = CreateTestFrameResult(defectCount: 1);

        var record = await sut.CaptureSnapshotAsync(frameResult);

        Path.GetDirectoryName(record.ImagePath).Should().Be(_capturesDir);
        File.Exists(record.ImagePath).Should().BeTrue();
    }

    [Fact]
    public async Task CaptureSnapshotAsync_SavesRawAndAnnotatedImages_AndPersistsInspectionRecordWithLiveCameraMode()
    {
        // Arrange
        var sut = CreateSut();
        using var frameResult = CreateTestFrameResult(defectCount: 2);

        // Act
        var record = await sut.CaptureSnapshotAsync(frameResult);

        // Assert - Return model
        record.Should().NotBeNull();
        record.Mode.Should().Be(DetectionMode.LiveCamera);
        record.DefectCount.Should().Be(2);
        record.IsPass.Should().BeFalse();
        record.Name.Should().NotBeNullOrWhiteSpace();

        // Assert - Both Raw and Annotated files exist on disk
        File.Exists(record.ImagePath).Should().BeTrue("Raw frame image must be saved to Captures directory");
        new FileInfo(record.ImagePath).Length.Should().BeGreaterThan(0);

        string annotatedPath = sut.GetAnnotatedImagePath(record.ImagePath);
        File.Exists(annotatedPath).Should().BeTrue("Annotated overlay image must be saved to Captures directory");
        new FileInfo(annotatedPath).Length.Should().BeGreaterThan(0);

        // Assert - Persisted in SQLite with full defect bounding boxes
        var persisted = await _repository.GetByIdAsync(record.Id);
        persisted.Should().NotBeNull();
        persisted!.Mode.Should().Be(DetectionMode.LiveCamera);
        persisted.ImagePath.Should().Be(record.ImagePath);
        persisted.Defects.Should().HaveCount(2);
        persisted.Defects[0].Class.Should().Be(DefectClass.Short);
        persisted.Defects[0].Confidence.Should().BeApproximately(0.92f, 0.001f);
        persisted.Defects[0].BoundingBox.X.Should().BeApproximately(0.15f, 0.001f);
        persisted.Defects[1].Class.Should().Be(DefectClass.HoleBreakout);
    }

    [Fact]
    public async Task CaptureLatestAsync_WhenEngineHasLatestFrame_CapturesAndPersistsRecord()
    {
        // Arrange
        var sut = CreateSut();
        using var frameResult = CreateTestFrameResult(defectCount: 0);
        _mockEngine.Setup(e => e.LatestResult).Returns(frameResult);

        // Act
        var record = await sut.CaptureLatestAsync("Custom-Board-A1");

        // Assert
        record.Name.Should().Be("Custom-Board-A1");
        record.Mode.Should().Be(DetectionMode.LiveCamera);
        record.IsPass.Should().BeTrue();
        record.DefectCount.Should().Be(0);

        var persisted = await _repository.GetByIdAsync(record.Id);
        persisted.Should().NotBeNull();
        persisted!.Name.Should().Be("Custom-Board-A1");
    }

    [Fact]
    public async Task CaptureLatestAsync_WhenEngineHasNoFrame_ThrowsCameraCaptureException()
    {
        // Arrange
        var sut = CreateSut();
        _mockEngine.Setup(e => e.LatestResult).Returns((LiveFrameResult?)null);

        // Act
        Func<Task> act = async () => await sut.CaptureLatestAsync();

        // Assert
        await act.Should().ThrowAsync<CameraCaptureException>()
            .WithMessage("*active*frame*");
    }

    [Fact]
    public async Task CaptureSnapshotAsync_WhenCalledTwiceOnSameFrame_AssignsDistinctPrimaryKeysWithoutCollision()
    {
        // Arrange
        var sut = CreateSut();
        using var frameResult = CreateTestFrameResult(defectCount: 1);

        // Act
        var record1 = await sut.CaptureSnapshotAsync(frameResult);
        var record2 = await sut.CaptureSnapshotAsync(frameResult);

        // Assert
        record1.Id.Should().NotBe(record2.Id);
        record1.Name.Should().NotBe(record2.Name);
        record1.ImagePath.Should().NotBe(record2.ImagePath);

        var totalCount = await _repository.GetTotalCountAsync();
        totalCount.Should().Be(2);
    }

    [Fact]
    public async Task CaptureSnapshotAsync_WhenRawFrameDisposed_ThrowsCameraCaptureException()
    {
        // Arrange
        var sut = CreateSut();
        var frameResult = CreateTestFrameResult(defectCount: 0);
        frameResult.Dispose();

        // Act
        Func<Task> act = async () => await sut.CaptureSnapshotAsync(frameResult);

        // Assert
        await act.Should().ThrowAsync<CameraCaptureException>();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup locks
        }
    }
}
