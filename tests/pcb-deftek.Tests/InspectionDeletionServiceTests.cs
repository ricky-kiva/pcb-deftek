using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Persistence;
using pcb_deftek.Infrastructure.Persistence.Migrations;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;
using Xunit;

namespace pcb_deftek.Tests;

public class InspectionDeletionServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _capturesRoot;
    private readonly string _databasePath;
    private readonly SqliteInspectionRepository _repository;
    private readonly InspectionDeletionService _service;

    public InspectionDeletionServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"pcb-deftek-delete-{Guid.NewGuid():N}");
        _capturesRoot = Path.Combine(_testRoot, "Captures");
        _databasePath = Path.Combine(_testRoot, "app.db");
        Directory.CreateDirectory(_capturesRoot);

        var factory = new SqliteConnectionFactory(_databasePath);
        new SqliteDatabaseMigrator(factory).MigrateToLatestAsync().GetAwaiter().GetResult();
        _repository = new SqliteInspectionRepository(factory);
        _service = new InspectionDeletionService(
            _repository,
            NullLogger<InspectionDeletionService>.Instance,
            _capturesRoot);
    }

    private static InspectionRecord CreateRecord(
        string name,
        DetectionMode mode,
        string imagePath,
        int defectCount = 0)
    {
        var id = Guid.NewGuid();
        var defects = new List<DetectedDefect>(defectCount);
        for (int i = 0; i < defectCount; i++)
        {
            defects.Add(new DetectedDefect(
                Guid.NewGuid(),
                id,
                DefectKnowledgeBase.GetInfo(DefectClass.Short).Code,
                0.9f,
                new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f)));
        }

        return new InspectionRecord(id, name, DateTime.UtcNow, DateTime.UtcNow, mode, imagePath, defects);
    }

    private (InspectionRecord Record, string RawPath, string AnnotatedPath) CreateLiveRecord(string name)
    {
        var id = Guid.NewGuid();
        var rawPath = Path.Combine(_capturesRoot, $"{id:N}_raw.png");
        var annotatedPath = Path.Combine(_capturesRoot, $"{id:N}_annotated.png");
        File.WriteAllBytes(rawPath, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(annotatedPath, new byte[] { 4, 5, 6 });

        var record = new InspectionRecord(
            id,
            name,
            DateTime.UtcNow,
            DateTime.UtcNow,
            DetectionMode.LiveCamera,
            rawPath,
            Array.Empty<DetectedDefect>());

        return (record, rawPath, annotatedPath);
    }

    [Fact]
    public async Task DeleteManyAsync_DeletesLiveCameraRecordAndAppOwnedFiles()
    {
        var (record, rawPath, annotatedPath) = CreateLiveRecord("live-board");
        await _repository.InsertAsync(record);

        var result = await _service.DeleteManyAsync(new[] { record.Id });

        result.DeletedRecordCount.Should().Be(1);
        result.DeletedFileCount.Should().Be(2);
        result.FailedFilePaths.Should().BeEmpty();
        (await _repository.GetByIdAsync(record.Id)).Should().BeNull();
        File.Exists(rawPath).Should().BeFalse();
        File.Exists(annotatedPath).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteManyAsync_DeletesSingleRecordThroughSingleDeleteApi()
    {
        var (record, rawPath, _) = CreateLiveRecord("single-board");
        await _repository.InsertAsync(record);

        var result = await _service.DeleteAsync(record.Id);

        result.DeletedRecordCount.Should().Be(1);
        (await _repository.GetByIdAsync(record.Id)).Should().BeNull();
        File.Exists(rawPath).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteManyAsync_DeletingSingleFileRecord_KeepsUserOriginal()
    {
        string originalPath = Path.Combine(_testRoot, "user-original.png");
        File.WriteAllBytes(originalPath, new byte[] { 7, 8, 9 });
        var record = CreateRecord("user-board", DetectionMode.SingleFile, originalPath, defectCount: 1);
        await _repository.InsertAsync(record);

        var result = await _service.DeleteManyAsync(new[] { record.Id });

        result.DeletedRecordCount.Should().Be(1);
        result.DeletedFileCount.Should().Be(0);
        (await _repository.GetByIdAsync(record.Id)).Should().BeNull();
        File.Exists(originalPath).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteManyAsync_LiveCameraPathOutsideCapturesRoot_DeletesRowButNotFile()
    {
        string outsidePath = Path.Combine(_testRoot, "outside", "capture.png");
        Directory.CreateDirectory(Path.GetDirectoryName(outsidePath)!);
        File.WriteAllBytes(outsidePath, new byte[] { 10, 11 });
        var record = CreateRecord("outside-board", DetectionMode.LiveCamera, outsidePath);
        await _repository.InsertAsync(record);

        var result = await _service.DeleteManyAsync(new[] { record.Id });

        result.DeletedRecordCount.Should().Be(1);
        result.DeletedFileCount.Should().Be(0);
        (await _repository.GetByIdAsync(record.Id)).Should().BeNull();
        File.Exists(outsidePath).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteManyAsync_PathTraversalOutsideCapturesRoot_DoesNotDeleteFile()
    {
        string outsidePath = Path.Combine(_testRoot, "traversal-outside.png");
        File.WriteAllBytes(outsidePath, new byte[] { 12, 13 });
        string traversalPath = Path.Combine(_capturesRoot, "..", "traversal-outside.png");
        var record = CreateRecord("traversal-board", DetectionMode.LiveCamera, traversalPath);
        await _repository.InsertAsync(record);

        var result = await _service.DeleteManyAsync(new[] { record.Id });

        result.DeletedRecordCount.Should().Be(1);
        result.DeletedFileCount.Should().Be(0);
        (await _repository.GetByIdAsync(record.Id)).Should().BeNull();
        File.Exists(outsidePath).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteManyAsync_MissingCaptureFile_IsIgnoredWithoutFailure()
    {
        string missingPath = Path.Combine(_capturesRoot, $"{Guid.NewGuid():N}_raw.png");
        var record = CreateRecord("missing-board", DetectionMode.LiveCamera, missingPath);
        await _repository.InsertAsync(record);

        var result = await _service.DeleteManyAsync(new[] { record.Id });

        result.DeletedRecordCount.Should().Be(1);
        result.DeletedFileCount.Should().Be(0);
        result.FailedFilePaths.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteManyAsync_CleanupFailure_IsReportedAfterCommittedRowDeletion()
    {
        var (record, rawPath, annotatedPath) = CreateLiveRecord("locked-board");
        await _repository.InsertAsync(record);
        File.SetAttributes(rawPath, FileAttributes.ReadOnly);

        try
        {
            var result = await _service.DeleteManyAsync(new[] { record.Id });

            result.DeletedRecordCount.Should().Be(1);
            result.FailedFilePaths.Should().Contain(rawPath);
            (await _repository.GetByIdAsync(record.Id)).Should().BeNull();
            File.Exists(rawPath).Should().BeTrue();
            File.Exists(annotatedPath).Should().BeFalse();
        }
        finally
        {
            File.SetAttributes(rawPath, FileAttributes.Normal);
            File.Delete(rawPath);
        }
    }

    [Fact]
    public async Task DeleteManyAsync_DatabaseFailure_DoesNotDeleteAnyFiles()
    {
        var (record, rawPath, annotatedPath) = CreateLiveRecord("rollback-board");
        var repository = new Mock<IInspectionRepository>();
        repository
            .Setup(repo => repo.GetSummariesByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new InspectionSummary(
                    record.Id,
                    record.Name,
                    record.CreatedAtUtc,
                    record.UpdatedAtUtc,
                    record.Mode,
                    0,
                    rawPath)
            });
        repository
            .Setup(repo => repo.DeleteManyAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PersistenceException("delete failed"));

        var service = new InspectionDeletionService(
            repository.Object,
            NullLogger<InspectionDeletionService>.Instance,
            _capturesRoot);

        Func<Task> act = async () => await service.DeleteManyAsync(new[] { record.Id });

        await act.Should().ThrowAsync<PersistenceException>();
        File.Exists(rawPath).Should().BeTrue();
        File.Exists(annotatedPath).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteManyAsync_WithEmptyIds_ReturnsEmptyResult()
    {
        var result = await _service.DeleteManyAsync(Array.Empty<Guid>());

        result.DeletedRecordCount.Should().Be(0);
        result.DeletedFileCount.Should().Be(0);
        result.FailedFilePaths.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteManyAsync_IgnoresDuplicateIds()
    {
        var (record, _, _) = CreateLiveRecord("duplicate-board");
        await _repository.InsertAsync(record);

        var result = await _service.DeleteManyAsync(new[] { record.Id, record.Id });

        result.DeletedRecordCount.Should().Be(1);
        result.DeletedFileCount.Should().Be(2);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_testRoot))
            {
                foreach (var file in Directory.EnumerateFiles(_testRoot, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
