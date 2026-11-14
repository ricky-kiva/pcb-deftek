using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Persistence;
using pcb_deftek.Infrastructure.Persistence.DatabaseExport;
using pcb_deftek.Infrastructure.Persistence.Migrations;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Presentation.Services;
using pcb_deftek.Presentation.ViewModels;
using Reqnroll;

namespace pcb_deftek.Bdd.StepDefinitions;

[Binding]
public class DashboardStepDefinitions : IDisposable
{
    private readonly string _testRoot;
    private readonly ISqliteConnectionFactory _factory;
    private readonly SqliteInspectionRepository _inspectionRepository;
    private readonly IUserSettingsService _settingsService;
    private readonly DashboardProgressService _progress = new();
    private DashboardViewModel _dashboard;

    public DashboardStepDefinitions()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"pcb-deftek-bdd-dashboard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
        string dbPath = Path.Combine(_testRoot, "dashboard.db");
        _factory = new Support.TestSqliteConnectionFactory(dbPath);
        new SqliteDatabaseMigrator(_factory).MigrateToLatestAsync().GetAwaiter().GetResult();
        _inspectionRepository = new SqliteInspectionRepository(_factory);
        _settingsService = new UserSettingsService(
            new SqliteSettingsRepository(_factory),
            new SqliteDatabaseExportService(_factory, Path.Combine(_testRoot, "exports")));
        _dashboard = CreateDashboard();
    }

    [Given(@"an isolated dashboard database")]
    public void GivenAnIsolatedDashboardDatabase() => Directory.Exists(_testRoot).Should().BeTrue();

    [Given(@"dashboard inspections across dates and classes")]
    public async Task GivenDashboardInspections()
    {
        await InsertAsync(LocalNoonUtc(), Array.Empty<DefectClass>());
        await InsertAsync(LocalNoonUtc().AddHours(-2), new[] { DefectClass.Short });
        await InsertAsync(LocalNoonUtc().AddDays(-10), new[] { DefectClass.Open });
    }

    [Given(@"a dashboard database with only an open defect")]
    public async Task GivenOnlyAnOpenDefect()
    {
        await InsertAsync(LocalNoonUtc(), new[] { DefectClass.Open });
    }

    [When(@"the dashboard loads")]
    public async Task WhenTheDashboardLoads() => await _dashboard.LoadAsync();

    [Then(@"the dashboard should report (.*) inspections and (.*) defects")]
    public void ThenDashboardReports(int inspections, int defects) => AssertDashboardTotals(inspections, defects);

    [Then(@"the dashboard should report (.*) inspection and (.*) defect")]
    public void ThenDashboardReportsSingle(int inspections, int defects) => AssertDashboardTotals(inspections, defects);

    private void AssertDashboardTotals(int inspections, int defects)
    {
        _dashboard.Statistics.Should().NotBeNull();
        _dashboard.Statistics!.TotalInspections.Should().Be(inspections);
        _dashboard.Statistics.TotalDefects.Should().Be(defects);
        _dashboard.MetricCards.Should().HaveCount(4);
        _dashboard.MetricCards[0].TimeSeries.Should().NotBeEmpty();
        _dashboard.MetricCards[1].TimeSeries.Should().NotBeEmpty();
        _dashboard.MetricCards[2].PassFailSeries.Should().NotBeEmpty();
        _dashboard.MetricCards[3].ClassCounts.Should().HaveCount(9);
    }

    [Then(@"the dashboard should use daily chart buckets")]
    public void ThenDashboardUsesDailyBuckets() =>
        _dashboard.Statistics!.BucketGranularity.Should().Be(DashboardBucketGranularity.Day);

    [When(@"I filter the dashboard to short defects")]
    public async Task WhenIFilterToShortDefects()
    {
        _dashboard.Filters.ClassOptions.Single(option => option.Value == DefectClass.Short).IsSelected = true;
        await _dashboard.ReloadAsync();
    }

    [When(@"I move the second dashboard card left and choose one column")]
    public async Task WhenIMoveCardAndChooseOneColumn()
    {
        await _dashboard.LoadAsync();
        await _dashboard.MoveCardLeftAsync(_dashboard.MetricCards[1]);
        _dashboard.SetOneColumnLayoutCommand.Execute(null);
        await WaitForSettingsAsync(settings => settings.DashboardColumnLayout == DashboardColumnLayout.OneColumn);
    }

    [When(@"I reload the dashboard")]
    public async Task WhenIReloadDashboard()
    {
        _dashboard = CreateDashboard();
        await _dashboard.LoadAsync();
    }

    [Then(@"the dashboard should use one column")]
    public void ThenDashboardUsesOneColumn() =>
        _dashboard.ColumnLayout.Should().Be(DashboardColumnLayout.OneColumn);

    [Then(@"the first dashboard card should be defects detected")]
    public void ThenFirstCardIsDefects() =>
        _dashboard.MetricCards[0].Key.Should().Be(DashboardCardOrderPolicy.TotalDefects);

    [Then(@"the dashboard should expose nine defect knowledge items")]
    public void ThenNineDefectItems() => _dashboard.DefectInfoItems.Should().HaveCount(9);

    [Then(@"every defect knowledge image should resolve from packaged resources")]
    public void ThenAllAssetsResolve()
    {
        foreach (var item in _dashboard.DefectInfoItems)
        {
            var uri = new Uri($"/pcb-deftek;component/{item.ImagePath}", UriKind.Relative);
            System.Windows.Application.GetResourceStream(uri).Should().NotBeNull(item.DisplayName);
        }
    }

    [When(@"I activate the first dashboard chart")]
    public void WhenIActivateFirstChart() =>
        _dashboard.OpenChartCommand.Execute(_dashboard.MetricCards[0]);

    [Then(@"the enlarged dashboard chart should be open")]
    public void ThenChartModalOpen() => _dashboard.IsChartModalOpen.Should().BeTrue();

    [When(@"I close the enlarged dashboard chart")]
    public void WhenICloseChart() => _dashboard.CloseChartCommand.Execute(null);

    [Then(@"the enlarged dashboard chart should be closed")]
    public void ThenChartModalClosed() => _dashboard.IsChartModalOpen.Should().BeFalse();

    [When(@"I open dashboard notifications")]
    public void WhenIOpenNotifications() => _dashboard.OpenNotificationsCommand.Execute(null);

    [Then(@"the dashboard notifications should be visible")]
    public void ThenNotificationsVisible() => _dashboard.IsNotificationModalOpen.Should().BeTrue();

    [When(@"dashboard inference and export progress report running")]
    public void WhenProgressReportsRunning()
    {
        _progress.ReportInference(DashboardProgressStatus.Running, 40, "Detecting");
        _progress.ReportDataExport(DashboardProgressStatus.Running, 25, "Exporting");
    }

    [Then(@"dashboard inference progress should be running at 40 percent")]
    public void ThenInferenceRunning()
    {
        _progress.Inference.Status.Should().Be(DashboardProgressStatus.Running);
        _progress.Inference.Percentage.Should().Be(40);
    }

    [Then(@"dashboard export progress should be running at 25 percent")]
    public void ThenExportRunning()
    {
        _progress.DataExport.Status.Should().Be(DashboardProgressStatus.Running);
        _progress.DataExport.Percentage.Should().Be(25);
    }

    [When(@"dashboard progress is reset")]
    public void WhenProgressReset() => _progress.Reset();

    [Then(@"dashboard progress should be idle at zero")]
    public void ThenProgressIdle()
    {
        _progress.Inference.Should().Be(DashboardProgressState.Idle);
        _progress.DataExport.Should().Be(DashboardProgressState.Idle);
    }

    [When(@"I enable skip dashboard on startup")]
    public async Task WhenIEnableStartupSkip()
    {
        _dashboard.SkipDashboardOnStartup = true;
        await WaitForSettingsAsync(settings => settings.SkipDashboardOnStartup);
    }

    [Then(@"the persisted startup preference should be enabled")]
    public async Task ThenStartupPersisted()
    {
        var settings = await _settingsService.LoadSettingsAsync();
        settings.SkipDashboardOnStartup.Should().BeTrue();
    }

    [Then(@"skip dashboard on startup should be enabled")]
    public void ThenStartupLoaded() => _dashboard.SkipDashboardOnStartup.Should().BeTrue();

    [Then(@"the dashboard should have no matching inspections")]
    public void ThenNoMatchingInspections()
    {
        _dashboard.Statistics!.TotalInspections.Should().Be(0);
        _dashboard.HasNoMatches.Should().BeTrue();
    }

    private DashboardViewModel CreateDashboard() => new(
        new SqliteDashboardRepository(_factory),
        _settingsService,
        _progress,
        NullLogger<DashboardViewModel>.Instance);

    private async Task InsertAsync(DateTime createdAtUtc, IReadOnlyList<DefectClass> defectClasses)
    {
        Guid id = Guid.NewGuid();
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
            DetectionMode.SingleFile,
            Path.Combine(_testRoot, $"{id:N}.png"),
            defects);
        await _inspectionRepository.InsertAsync(record);
    }

    private async Task WaitForSettingsAsync(Func<UserSettings, bool> condition)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (condition(await _settingsService.LoadSettingsAsync()))
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Dashboard settings were not persisted.");
    }

    private static DateTime LocalNoonUtc() =>
        DateTime.SpecifyKind(DateTime.Today.AddHours(12), DateTimeKind.Local).ToUniversalTime();

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
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }
}
