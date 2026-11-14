using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Persistence;
using pcb_deftek.Infrastructure.Persistence.Migrations;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using Xunit;

namespace pcb_deftek.Tests;

public class DashboardRepositoryTests : IDisposable
{
    private readonly string _testRoot;
    private readonly SqliteInspectionRepository _inspectionRepository;
    private readonly SqliteDashboardRepository _dashboardRepository;

    public DashboardRepositoryTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"pcb-deftek-dashboard-{Guid.NewGuid():N}");
        string dbPath = Path.Combine(_testRoot, "dashboard.db");
        Directory.CreateDirectory(_testRoot);
        var factory = new SqliteConnectionFactory(dbPath);
        new SqliteDatabaseMigrator(factory).MigrateToLatestAsync().GetAwaiter().GetResult();
        _inspectionRepository = new SqliteInspectionRepository(factory);
        _dashboardRepository = new SqliteDashboardRepository(factory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task NoFilters_ReturnsTotalsBucketsAndNineClassDistribution()
    {
        var now = DateTime.UtcNow;
        await Insert(now.AddDays(-5), Array.Empty<DefectClass>());
        await Insert(now.AddDays(-4), new[] { DefectClass.Short, DefectClass.Short });
        await Insert(now.AddDays(-1), new[] { DefectClass.Open });

        var result = await _dashboardRepository.GetStatisticsAsync(DashboardFilter.AllTime);

        result.TotalInspections.Should().Be(3);
        result.TotalDefects.Should().Be(3);
        result.PassCount.Should().Be(1);
        result.FailCount.Should().Be(2);
        result.BucketGranularity.Should().Be(DashboardBucketGranularity.Day);
        result.Buckets.Should().NotBeEmpty();
        result.Buckets.Sum(bucket => bucket.Inspections).Should().Be(3);
        result.ClassCounts.Should().HaveCount(9);
        result.ClassCounts.Single(item => item.Class == DefectClass.Short).Count.Should().Be(2);
        result.ClassCounts.Single(item => item.Class == DefectClass.Open).Count.Should().Be(1);
        result.ClassCounts.Sum(item => item.Count).Should().Be(3);
    }

    [Fact]
    public async Task ClassFilter_CountsOnlyMatchingDefectsAndExcludesPassInspections()
    {
        var now = DateTime.UtcNow;
        await Insert(now.AddDays(-4), Array.Empty<DefectClass>());
        await Insert(now.AddDays(-3), new[] { DefectClass.Short, DefectClass.Open });

        var result = await _dashboardRepository.GetStatisticsAsync(
            new DashboardFilter(defectClasses: new[] { DefectClass.Open }));

        result.TotalInspections.Should().Be(1);
        result.TotalDefects.Should().Be(1);
        result.PassCount.Should().Be(0);
        result.FailCount.Should().Be(1);
        result.ClassCounts.Single(item => item.Class == DefectClass.Open).Count.Should().Be(1);
        result.ClassCounts.Single(item => item.Class == DefectClass.Short).Count.Should().Be(0);
    }

    [Fact]
    public async Task SeverityFilter_MapsThroughKnowledgeBase()
    {
        var now = DateTime.UtcNow;
        await Insert(now.AddDays(-4), new[] { DefectClass.Open });
        await Insert(now.AddDays(-3), new[] { DefectClass.Short });

        var result = await _dashboardRepository.GetStatisticsAsync(
            new DashboardFilter(severities: new[] { DefectSeverity.Critical }));

        result.TotalInspections.Should().Be(1);
        result.TotalDefects.Should().Be(1);
        result.ClassCounts.Single(item => item.Class == DefectClass.Short).Count.Should().Be(1);
        result.ClassCounts.Single(item => item.Class == DefectClass.Open).Count.Should().Be(0);
    }

    [Fact]
    public async Task ClassAndSeverityFilters_ComposeWithAnd()
    {
        var now = DateTime.UtcNow;
        await Insert(now.AddDays(-4), new[] { DefectClass.Short });
        await Insert(now.AddDays(-3), new[] { DefectClass.Open });

        var matching = await _dashboardRepository.GetStatisticsAsync(
            new DashboardFilter(
                defectClasses: new[] { DefectClass.Short },
                severities: new[] { DefectSeverity.Critical }));
        var nonMatching = await _dashboardRepository.GetStatisticsAsync(
            new DashboardFilter(
                defectClasses: new[] { DefectClass.Open },
                severities: new[] { DefectSeverity.Critical }));

        matching.TotalInspections.Should().Be(1);
        matching.TotalDefects.Should().Be(1);
        nonMatching.TotalInspections.Should().Be(0);
        nonMatching.TotalDefects.Should().Be(0);
    }

    [Fact]
    public async Task AllNineClassesWithNoSeverity_BehavesLikeNoClassFilter()
    {
        var now = DateTime.UtcNow;
        await Insert(now.AddDays(-4), Array.Empty<DefectClass>());
        await Insert(now.AddDays(-3), new[] { DefectClass.Open });

        var result = await _dashboardRepository.GetStatisticsAsync(
            new DashboardFilter(defectClasses: DefectKnowledgeBase.GetAll().Select(info => info.Class)));

        result.TotalInspections.Should().Be(2);
        result.PassCount.Should().Be(1);
        result.FailCount.Should().Be(1);
    }

    [Fact]
    public async Task ExplicitMonthlyOverride_GroupsEvenAShortDataSpanByMonth()
    {
        DateTime start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await Insert(start.AddDays(10), new[] { DefectClass.Open });
        await Insert(start.AddDays(20), new[] { DefectClass.Short });

        var result = await _dashboardRepository.GetStatisticsAsync(
            new DashboardFilter(
                start,
                start.AddDays(30),
                bucketGranularityOverride: DashboardBucketGranularity.Month));

        result.BucketGranularity.Should().Be(DashboardBucketGranularity.Month);
        result.TotalInspections.Should().Be(2);
    }

    [Fact]
    public async Task DateBounds_AreInclusive()
    {
        DateTime start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await Insert(start, new[] { DefectClass.Open });
        await Insert(start.AddDays(1), new[] { DefectClass.Short });

        var result = await _dashboardRepository.GetStatisticsAsync(
            new DashboardFilter(start, start.AddDays(1)));

        result.TotalInspections.Should().Be(2);
        result.TotalDefects.Should().Be(2);
    }

    [Theory]
    [InlineData(0, DashboardBucketGranularity.Hour)]
    [InlineData(10, DashboardBucketGranularity.Day)]
    [InlineData(100, DashboardBucketGranularity.Week)]
    [InlineData(800, DashboardBucketGranularity.Month)]
    public async Task AdaptiveBucketing_UsesDataExtent(int spanDays, DashboardBucketGranularity expected)
    {
        DateTime start = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        await Insert(start, new[] { DefectClass.Open });
        if (spanDays > 0)
        {
            await Insert(start.AddDays(spanDays), new[] { DefectClass.Short });
        }

        var result = await _dashboardRepository.GetStatisticsAsync(DashboardFilter.AllTime);

        result.BucketGranularity.Should().Be(expected);
        result.Buckets.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SeededHundredRecords_ReturnsConsistentTotals()
    {
        DateTime start = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var defectClasses = DefectKnowledgeBase.GetAll().Select(info => info.Class).ToArray();
        int expectedDefects = 0;

        for (int i = 0; i < 120; i++)
        {
            var classes = i % 4 == 0
                ? Array.Empty<DefectClass>()
                : new[] { defectClasses[i % defectClasses.Length] };
            expectedDefects += classes.Length;
            await Insert(start.AddHours(i), classes);
        }

        var result = await _dashboardRepository.GetStatisticsAsync(DashboardFilter.AllTime);

        result.TotalInspections.Should().Be(120);
        result.TotalDefects.Should().Be(expectedDefects);
        result.PassCount.Should().Be(30);
        result.FailCount.Should().Be(90);
        result.ClassCounts.Sum(item => item.Count).Should().Be(expectedDefects);
        result.Buckets.Sum(bucket => bucket.Inspections).Should().Be(120);
    }

    [Fact]
    public async Task NoFilters_FillsEmptyBucketsBetweenInspectionDays()
    {
        var now = DateTime.UtcNow;
        await Insert(now.AddDays(-10), Array.Empty<DefectClass>());
        await Insert(now, new[] { DefectClass.Open });

        var result = await _dashboardRepository.GetStatisticsAsync(DashboardFilter.AllTime);

        result.BucketGranularity.Should().Be(DashboardBucketGranularity.Day);
        result.Buckets.Should().HaveCountGreaterThanOrEqualTo(11);
        result.Buckets.Should().Contain(bucket => bucket.Inspections == 0);
        result.Buckets.Sum(bucket => bucket.Inspections).Should().Be(2);
    }

    [Fact]
    public async Task EmptyDatabase_ReturnsEmptyStatistics()
    {
        var result = await _dashboardRepository.GetStatisticsAsync(DashboardFilter.AllTime);

        result.HasData.Should().BeFalse();
        result.Buckets.Should().BeEmpty();
        result.ClassCounts.Should().HaveCount(9);
    }

    [Fact]
    public async Task MatchingInspections_ReturnNewestFirstWithMetadataAndDefects()
    {
        var now = DateTime.UtcNow;
        var oldest = await Insert(now.AddDays(-3), Array.Empty<DefectClass>(), DetectionMode.BatchFolder, InspectionOrigin.Crop);
        var middle = await Insert(now.AddDays(-2), new[] { DefectClass.Short, DefectClass.Open });
        var newest = await Insert(now.AddDays(-1), new[] { DefectClass.Open });

        var result = await _dashboardRepository.GetMatchingInspectionsAsync(DashboardFilter.AllTime);

        result.Select(record => record.Id).Should().Equal(newest.Id, middle.Id, oldest.Id);
        result[0].Name.Should().Be(newest.Name);
        result[0].ImagePath.Should().Be(newest.ImagePath);
        result[0].Mode.Should().Be(DetectionMode.SingleFile);
        result[0].Origin.Should().Be(InspectionOrigin.External);
        result[0].Defects.Select(defect => defect.Class).Should().Equal(DefectClass.Open);
        result[1].Defects.Select(defect => defect.Class).Should().BeEquivalentTo(new[] { DefectClass.Short, DefectClass.Open });
        result[2].Defects.Should().BeEmpty();
        result[2].Origin.Should().Be(InspectionOrigin.Crop);
        result[2].Mode.Should().Be(DetectionMode.BatchFolder);
    }

    [Fact]
    public async Task ClassFilter_ProjectsOnlyMatchingDefectsAndExcludesPassInspections()
    {
        var now = DateTime.UtcNow;
        await Insert(now.AddDays(-3), Array.Empty<DefectClass>());
        await Insert(now.AddDays(-2), new[] { DefectClass.Short });
        var openOnly = await Insert(now.AddDays(-1), new[] { DefectClass.Open });
        var filter = new DashboardFilter(defectClasses: new[] { DefectClass.Open });

        var statistics = await _dashboardRepository.GetStatisticsAsync(filter);
        var count = await _dashboardRepository.GetMatchingInspectionCountAsync(filter);
        var evidence = await _dashboardRepository.GetMatchingInspectionsAsync(filter);

        count.Should().Be(1);
        evidence.Should().ContainSingle();
        evidence[0].Id.Should().Be(openOnly.Id);
        evidence[0].Defects.Should().ContainSingle()
            .Which.Class.Should().Be(DefectClass.Open);
        evidence[0].DefectCount.Should().Be(statistics.TotalDefects);
        evidence[0].DefectCount.Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EvidenceCountAndStatistics_AgreeForFilterCombinations(bool includeSeverity)
    {
        var now = DateTime.UtcNow;
        await Insert(now.AddDays(-8), Array.Empty<DefectClass>());
        await Insert(now.AddDays(-7), new[] { DefectClass.Short });
        await Insert(now.AddDays(-6), new[] { DefectClass.Open, DefectClass.Short });
        await Insert(now.AddDays(-5), new[] { DefectClass.Spur });
        await Insert(now.AddDays(-2), new[] { DefectClass.Open });
        await Insert(now.AddDays(-1), new[] { DefectClass.Short, DefectClass.Open });

        var filters = new List<DashboardFilter>
        {
            DashboardFilter.AllTime,
            new(startDateUtc: now.AddDays(-6), endDateUtc: now.AddDays(-2)),
            new(defectClasses: new[] { DefectClass.Short }),
            new(defectClasses: new[] { DefectClass.Short, DefectClass.Open })
        };
        if (includeSeverity)
        {
            filters.Add(new DashboardFilter(severities: new[] { DefectSeverity.High }));
            filters.Add(new DashboardFilter(
                defectClasses: new[] { DefectClass.Short, DefectClass.Open },
                severities: new[] { DefectSeverity.High }));
        }

        foreach (var filter in filters)
        {
            var statistics = await _dashboardRepository.GetStatisticsAsync(filter);
            var count = await _dashboardRepository.GetMatchingInspectionCountAsync(filter);
            var evidence = await _dashboardRepository.GetMatchingInspectionsAsync(filter);

            count.Should().Be(evidence.Count);
            count.Should().Be(statistics.TotalInspections);
            evidence.Sum(record => record.DefectCount).Should().Be(statistics.TotalDefects);
        }
    }

    [Fact]
    public async Task MatchingInspections_UseIdAsDeterministicTiebreaker()
    {
        var at = DateTime.UtcNow;
        var first = await Insert(at, new[] { DefectClass.Open });
        var second = await Insert(at, new[] { DefectClass.Short });

        var result = await _dashboardRepository.GetMatchingInspectionsAsync(DashboardFilter.AllTime);

        var expected = new[] { first.Id, second.Id }
            .OrderByDescending(id => id.ToString("D"), StringComparer.Ordinal);
        result.Select(record => record.Id).Should().Equal(expected);
    }

    [Fact]
    public async Task DateBounds_AreInclusiveForEvidenceAndCount()
    {
        var start = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var end = start.AddDays(2);
        await Insert(start.AddTicks(-1), new[] { DefectClass.Open });
        var atStart = await Insert(start, new[] { DefectClass.Open });
        var atEnd = await Insert(end, new[] { DefectClass.Open });
        await Insert(end.AddTicks(1), new[] { DefectClass.Open });
        var filter = new DashboardFilter(startDateUtc: start, endDateUtc: end);

        var evidence = await _dashboardRepository.GetMatchingInspectionsAsync(filter);
        var count = await _dashboardRepository.GetMatchingInspectionCountAsync(filter);

        count.Should().Be(2);
        evidence.Select(record => record.Id).Should().Equal(atEnd.Id, atStart.Id);
    }

    [Fact]
    public async Task EmptyDatabase_ReturnsNoEvidenceAndZeroCount()
    {
        var evidence = await _dashboardRepository.GetMatchingInspectionsAsync(DashboardFilter.AllTime);
        var count = await _dashboardRepository.GetMatchingInspectionCountAsync(DashboardFilter.AllTime);

        evidence.Should().BeEmpty();
        count.Should().Be(0);
    }

    private async Task<InspectionRecord> Insert(
        DateTime createdAtUtc,
        IReadOnlyList<DefectClass> defectClasses,
        DetectionMode mode = DetectionMode.SingleFile,
        InspectionOrigin origin = InspectionOrigin.External)
    {
        var id = Guid.NewGuid();
        var defects = defectClasses.Select(defectClass => new DetectedDefect(
            Guid.NewGuid(),
            id,
            DefectKnowledgeBase.GetInfo(defectClass).Code,
            0.9f,
            new BoundingBox(0.1f, 0.1f, 0.2f, 0.2f))).ToArray();

        var record = new InspectionRecord(
            id,
            $"board-{id:N}",
            createdAtUtc,
            createdAtUtc,
            mode,
            $"images/{id:N}.png",
            defects,
            origin);
        await _inspectionRepository.InsertAsync(record);
        return record;
    }
}
