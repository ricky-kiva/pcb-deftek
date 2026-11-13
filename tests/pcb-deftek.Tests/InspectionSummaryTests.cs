using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

public class InspectionSummaryTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteInspectionRepository _repository;

    public InspectionSummaryTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"pcb-deftek-summary-{Guid.NewGuid():N}.db");
        var factory = new SqliteConnectionFactory(_tempDbPath);
        new SqliteDatabaseMigrator(factory).MigrateToLatestAsync().GetAwaiter().GetResult();
        _repository = new SqliteInspectionRepository(factory);
    }

    private static InspectionRecord CreateRecord(
        string name,
        DetectionMode mode,
        DateTime createdAtUtc,
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

        return new InspectionRecord(
            id,
            name,
            createdAtUtc,
            createdAtUtc,
            mode,
            $"images/{name}.png",
            defects);
    }

    [Fact]
    public async Task GetSummariesPagedAsync_ReturnsStoredDefectCountAndStatusWithoutLoadingDefects()
    {
        var pass = CreateRecord("pass-board", DetectionMode.SingleFile, DateTime.UtcNow, defectCount: 0);
        var fail = CreateRecord("fail-board", DetectionMode.LiveCamera, DateTime.UtcNow.AddMinutes(-1), defectCount: 2);
        await _repository.InsertAsync(pass);
        await _repository.InsertAsync(fail);

        var page = await _repository.GetSummariesPagedAsync(new InspectionQueryFilter { PageSize = 10 });

        page.TotalItems.Should().Be(2);
        var failSummary = page.Items.Single(item => item.Id == fail.Id);
        failSummary.DefectCount.Should().Be(2);
        failSummary.Status.Should().Be(InspectionStatus.Fail);
        failSummary.IsPass.Should().BeFalse();

        var passSummary = page.Items.Single(item => item.Id == pass.Id);
        passSummary.DefectCount.Should().Be(0);
        passSummary.Status.Should().Be(InspectionStatus.Pass);
        passSummary.IsPass.Should().BeTrue();
    }

    [Fact]
    public async Task GetSummariesPagedAsync_OrdersByCreatedAtDescending()
    {
        var oldest = CreateRecord("oldest", DetectionMode.SingleFile, DateTime.UtcNow.AddDays(-2));
        var middle = CreateRecord("middle", DetectionMode.BatchFolder, DateTime.UtcNow.AddDays(-1));
        var newest = CreateRecord("newest", DetectionMode.LiveCamera, DateTime.UtcNow);
        await _repository.InsertAsync(oldest);
        await _repository.InsertAsync(middle);
        await _repository.InsertAsync(newest);

        var page = await _repository.GetSummariesPagedAsync(new InspectionQueryFilter { PageSize = 10 });

        page.Items.Select(item => item.Id).Should().ContainInOrder(newest.Id, middle.Id, oldest.Id);
    }

    [Fact]
    public async Task GetSummariesPagedAsync_HonorsPagingAndReportsTotalItems()
    {
        for (int i = 0; i < 3; i++)
        {
            await _repository.InsertAsync(
                CreateRecord($"board-{i}", DetectionMode.SingleFile, DateTime.UtcNow.AddMinutes(-i)));
        }

        var page = await _repository.GetSummariesPagedAsync(new InspectionQueryFilter
        {
            PageNumber = 1,
            PageSize = 2
        });

        page.Items.Should().HaveCount(2);
        page.TotalItems.Should().Be(3);
        page.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public async Task GetSummariesByIdsAsync_ReturnsOnlyRequestedSummaries()
    {
        var first = CreateRecord("first", DetectionMode.SingleFile, DateTime.UtcNow);
        var second = CreateRecord("second", DetectionMode.SingleFile, DateTime.UtcNow);
        var third = CreateRecord("third", DetectionMode.SingleFile, DateTime.UtcNow);
        await _repository.InsertAsync(first);
        await _repository.InsertAsync(second);
        await _repository.InsertAsync(third);

        var summaries = await _repository.GetSummariesByIdsAsync(new[] { first.Id, third.Id });

        summaries.Should().HaveCount(2);
        summaries.Select(item => item.Id).Should().BeEquivalentTo(new[] { first.Id, third.Id });
    }

    [Fact]
    public async Task GetSummariesByIdsAsync_WithEmptyIds_ReturnsEmpty()
    {
        var summaries = await _repository.GetSummariesByIdsAsync(Array.Empty<Guid>());

        summaries.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteManyAsync_DeletesRequestedRowsAndCascadesDefects()
    {
        var keep = CreateRecord("keep", DetectionMode.SingleFile, DateTime.UtcNow, defectCount: 1);
        var remove = CreateRecord("remove", DetectionMode.LiveCamera, DateTime.UtcNow, defectCount: 1);
        await _repository.InsertAsync(keep);
        await _repository.InsertAsync(remove);

        var deleted = await _repository.DeleteManyAsync(new[] { remove.Id });

        deleted.Should().Be(1);
        (await _repository.GetByIdAsync(remove.Id)).Should().BeNull();
        (await _repository.GetByIdAsync(keep.Id)).Should().NotBeNull();
        (await _repository.GetTotalCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DeleteManyAsync_WithEmptyIds_ReturnsZero()
    {
        var deleted = await _repository.DeleteManyAsync(Array.Empty<Guid>());

        deleted.Should().Be(0);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_tempDbPath))
            {
                File.Delete(_tempDbPath);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
