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

public class InspectionFilterQueryTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteInspectionRepository _repository;
    private readonly DateTime _day1;
    private readonly InspectionRecord _passRecord;
    private readonly InspectionRecord _shortAndOpenRecord;
    private readonly InspectionRecord _spurRecord;

    public InspectionFilterQueryTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"pcb-deftek-filter-{Guid.NewGuid():N}.db");
        var factory = new SqliteConnectionFactory(_tempDbPath);
        new SqliteDatabaseMigrator(factory).MigrateToLatestAsync().GetAwaiter().GetResult();
        _repository = new SqliteInspectionRepository(factory);

        _day1 = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        _passRecord = CreateRecord("pass", _day1, DetectionMode.SingleFile);
        _shortAndOpenRecord = CreateRecord(
            "short-open",
            _day1.AddDays(1),
            DetectionMode.LiveCamera,
            DefectClass.Short,
            DefectClass.Open);
        _spurRecord = CreateRecord(
            "spur",
            _day1.AddDays(2),
            DetectionMode.BatchFolder,
            DefectClass.Spur);

        _repository.InsertAsync(_passRecord).GetAwaiter().GetResult();
        _repository.InsertAsync(_shortAndOpenRecord).GetAwaiter().GetResult();
        _repository.InsertAsync(_spurRecord).GetAwaiter().GetResult();
    }

    private static InspectionRecord CreateRecord(
        string name,
        DateTime createdAtUtc,
        DetectionMode mode,
        params DefectClass[] defectClasses)
    {
        var id = Guid.NewGuid();
        var defects = defectClasses
            .Select(defectClass => new DetectedDefect(
                Guid.NewGuid(),
                id,
                DefectKnowledgeBase.GetInfo(defectClass).Code,
                0.9f,
                new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f)))
            .ToList();

        return new InspectionRecord(id, name, createdAtUtc, createdAtUtc, mode, $"images/{name}.png", defects);
    }

    [Fact]
    public async Task Summaries_WithPassStatus_ReturnsOnlyDefectlessRecords()
    {
        var page = await _repository.GetSummariesPagedAsync(new InspectionQueryFilter
        {
            Statuses = new[] { InspectionStatus.Pass }
        });

        page.Items.Select(item => item.Id).Should().BeEquivalentTo(new[] { _passRecord.Id });
        page.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task Summaries_WithFailStatus_ReturnsOnlyRecordsWithDefects()
    {
        var page = await _repository.GetSummariesPagedAsync(new InspectionQueryFilter
        {
            Statuses = new[] { InspectionStatus.Fail }
        });

        page.Items.Select(item => item.Id).Should()
            .BeEquivalentTo(new[] { _shortAndOpenRecord.Id, _spurRecord.Id });
    }

    [Fact]
    public async Task Summaries_WithBothStatuses_DoesNotApplyStatusClause()
    {
        var page = await _repository.GetSummariesPagedAsync(new InspectionQueryFilter
        {
            Statuses = new[] { InspectionStatus.Pass, InspectionStatus.Fail }
        });

        page.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task Summaries_LegacyStatusPropertyStillAppliesWhenStatusesIsNotSet()
    {
        var page = await _repository.GetSummariesPagedAsync(new InspectionQueryFilter
        {
            Status = InspectionStatus.Fail
        });

        page.Items.Should().HaveCount(2);
        page.Items.Should().OnlyContain(item => item.DefectCount > 0);
    }

    [Fact]
    public async Task Summaries_StatusesTakePrecedenceOverLegacyStatus()
    {
        var page = await _repository.GetSummariesPagedAsync(new InspectionQueryFilter
        {
            Status = InspectionStatus.Pass,
            Statuses = new[] { InspectionStatus.Fail }
        });

        page.Items.Should().HaveCount(2);
        page.Items.Should().OnlyContain(item => item.DefectCount > 0);
    }

    [Fact]
    public async Task Summaries_WithSingleClass_ReturnsRecordsContainingThatClass()
    {
        var page = await _repository.GetSummariesPagedAsync(new InspectionQueryFilter
        {
            DefectClasses = new[] { DefectClass.Short }
        });

        page.Items.Select(item => item.Id).Should().BeEquivalentTo(new[] { _shortAndOpenRecord.Id });
    }

    [Fact]
    public async Task Summaries_WithMultipleClasses_MatchesAnySelectedClass()
    {
        var page = await _repository.GetSummariesPagedAsync(new InspectionQueryFilter
        {
            DefectClasses = new[] { DefectClass.Open, DefectClass.Spur }
        });

        page.Items.Select(item => item.Id).Should()
            .BeEquivalentTo(new[] { _shortAndOpenRecord.Id, _spurRecord.Id });
    }

    [Fact]
    public async Task Summaries_ClassFilterExcludesDefectlessRecords()
    {
        var page = await _repository.GetSummariesPagedAsync(new InspectionQueryFilter
        {
            DefectClasses = Enum.GetValues<DefectClass>()
        });

        page.Items.Should().HaveCount(2);
        page.Items.Should().NotContain(item => item.Id == _passRecord.Id);
    }

    [Fact]
    public async Task Summaries_WithEmptySets_AppliesNoStatusOrClassClause()
    {
        var page = await _repository.GetSummariesPagedAsync(new InspectionQueryFilter
        {
            Statuses = Array.Empty<InspectionStatus>(),
            DefectClasses = Array.Empty<DefectClass>()
        });

        page.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task Summaries_CombinedDateStatusAndClassFilters_ComposeWithAnd()
    {
        var page = await _repository.GetSummariesPagedAsync(new InspectionQueryFilter
        {
            StartDateUtc = _day1.AddDays(1),
            EndDateUtc = _day1.AddDays(1),
            Statuses = new[] { InspectionStatus.Fail },
            DefectClasses = new[] { DefectClass.Short }
        });

        page.Items.Select(item => item.Id).Should().BeEquivalentTo(new[] { _shortAndOpenRecord.Id });
        page.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task Summaries_WithFilters_ReportsFilteredTotalsAcrossPages()
    {
        var page = await _repository.GetSummariesPagedAsync(new InspectionQueryFilter
        {
            Statuses = new[] { InspectionStatus.Fail },
            PageNumber = 1,
            PageSize = 1
        });

        page.Items.Should().HaveCount(1);
        page.TotalItems.Should().Be(2);
        page.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public async Task PagedRecords_WithClassFilter_ReturnsOnlyMatchingAggregates()
    {
        var page = await _repository.GetPagedAsync(new InspectionQueryFilter
        {
            DefectClasses = new[] { DefectClass.Spur }
        });

        page.Items.Should().ContainSingle();
        page.Items[0].Id.Should().Be(_spurRecord.Id);
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
