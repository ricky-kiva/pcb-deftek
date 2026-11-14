using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Presentation.Models;
using pcb_deftek.Presentation.Services;
using pcb_deftek.Presentation.ViewModels;
using Xunit;

namespace pcb_deftek.Tests;

public class DashboardViewModelTests
{
    private readonly FakeDashboardRepository _repository = new();
    private readonly FakeUserSettingsService _settings = new();
    private readonly DashboardProgressService _progress = new();
    private readonly FakeNotificationService _notification = new();

    [Fact]
    public void Constructor_BuildsNineDefectInfoItemsInClassOrderWithAssetPaths()
    {
        var sut = CreateSut();

        sut.DefectInfoItems.Should().HaveCount(9);
        sut.DefectInfoItems.Select(item => item.ClassId).Should().BeInAscendingOrder();
        sut.DefectInfoItems.Should().OnlyContain(item => item.ImagePath.StartsWith("Assets/Images/dashb-info-", StringComparison.Ordinal));
        sut.DefectInfoItems.Select(item => item.ImagePath).Should().Equal(
            "Assets/Images/dashb-info-sh.png",
            "Assets/Images/dashb-info-sp.png",
            "Assets/Images/dashb-info-sc.png",
            "Assets/Images/dashb-info-op.png",
            "Assets/Images/dashb-info-mb.png",
            "Assets/Images/dashb-info-hb.png",
            "Assets/Images/dashb-info-cs.png",
            "Assets/Images/dashb-info-cfo.png",
            "Assets/Images/dashb-info-bmfo.png");
        sut.DefectInfoItems.Select(item => item.SeverityLabel).Should().OnlyContain(label => !string.IsNullOrWhiteSpace(label));
    }

    [Fact]
    public async Task LoadAsync_LoadsAllTimeStatisticsAndBuildsFourDefaultCards()
    {
        var sut = CreateSut();
        _repository.Handler = (_, _) => Task.FromResult(SampleStatistics());

        await sut.LoadAsync();

        _repository.Requests.Should().ContainSingle();
        _repository.Requests[0].StartDateUtc.Should().BeNull();
        _repository.Requests[0].DefectClasses.Should().BeEmpty();
        sut.Statistics!.TotalInspections.Should().Be(4);
        sut.HasData.Should().BeTrue();
        sut.IsEmpty.Should().BeFalse();
        sut.IsLoading.Should().BeFalse();
        sut.MetricCards.Select(card => card.Key).Should().Equal(
            DashboardCardOrderPolicy.TotalPcbs,
            DashboardCardOrderPolicy.TotalDefects,
            DashboardCardOrderPolicy.PassFail,
            DashboardCardOrderPolicy.Distribution);
        sut.MetricCards[0].Title.Should().Be("PCBs Inspected");
        sut.MetricCards[1].Title.Should().Be("Defects Detected");
        sut.MetricCards[2].Title.Should().Be("Inspection Rate");
        sut.MetricCards[3].Title.Should().Be("Defect Distribution");
        sut.MetricCards[0].DisplayValue.Should().Be("4");
        sut.MetricCards[1].DisplayValue.Should().Be("2");
        sut.MetricCards[2].PassCount.Should().Be(2);
        sut.MetricCards[2].FailCount.Should().Be(2);
        sut.MetricCards[3].ShowsPrimaryValue.Should().BeFalse();
        sut.MetricCards[2].PassFailSeries.Should().HaveCount(2);
        sut.MetricCards[3].ClassCounts.Should().HaveCount(9);
        sut.ColumnLayout.Should().Be(DashboardColumnLayout.TwoColumns);
    }

    [Fact]
    public async Task LoadAsync_AppliesPersistedCardOrderLayoutAndStartupPreference()
    {
        _settings.Settings = new UserSettings
        {
            DashboardCardOrder = new[]
            {
                DashboardCardOrderPolicy.Distribution,
                DashboardCardOrderPolicy.PassFail,
                DashboardCardOrderPolicy.TotalDefects
            },
            DashboardColumnLayout = DashboardColumnLayout.OneColumn,
            SkipDashboardOnStartup = true
        };
        _repository.Handler = (_, _) => Task.FromResult(SampleStatistics());
        var sut = CreateSut();

        await sut.LoadAsync();

        sut.MetricCards.Select(card => card.Key).Should().Equal(
            DashboardCardOrderPolicy.Distribution,
            DashboardCardOrderPolicy.PassFail,
            DashboardCardOrderPolicy.TotalDefects,
            DashboardCardOrderPolicy.TotalPcbs);
        sut.ColumnLayout.Should().Be(DashboardColumnLayout.OneColumn);
        sut.IsTwoColumnLayout.Should().BeFalse();
        sut.SkipDashboardOnStartup.Should().BeTrue();
    }

    [Fact]
    public async Task FilterChange_ReloadsWithProjectedFilter()
    {
        _repository.Handler = (_, _) => Task.FromResult(SampleStatistics());
        var sut = CreateSut();
        await sut.LoadAsync();

        sut.Filters.ClassOptions.Single(option => option.Value == DefectClass.Short).IsSelected = true;
        await WaitUntilAsync(() => _repository.Requests.Count >= 2);

        _repository.Requests.Last().DefectClasses.Should().Equal(DefectClass.Short);
        sut.Statistics!.TotalInspections.Should().Be(4);
    }

    [Fact]
    public async Task InvalidCustomRange_DoesNotQuery()
    {
        var sut = CreateSut();

        sut.Filters.SelectedPreset = DateRangePreset.Custom;
        sut.Filters.CustomStartDate = new DateTime(2026, 11, 5);
        sut.Filters.CustomEndDate = new DateTime(2026, 11, 1);
        await Task.Delay(30);

        _repository.Requests.Should().BeEmpty();
        sut.Filters.TryCreateFilter(out _).Should().BeFalse();
    }

    [Fact]
    public async Task PersistingDashboardPreferences_PreservesUnrelatedSettings()
    {
        _settings.Settings = new UserSettings
        {
            ConfidenceThreshold = 0.55,
            PreferredDevice = "GpuDirectML",
            ReportExportPath = @"D:\Reports"
        };
        _repository.Handler = (_, _) => Task.FromResult(SampleStatistics());
        var sut = CreateSut();
        await sut.LoadAsync();

        await sut.MoveCardLeftAsync(sut.MetricCards[1]);

        _settings.Saved.Last().ConfidenceThreshold.Should().Be(0.55);
        _settings.Saved.Last().PreferredDevice.Should().Be("GpuDirectML");
        _settings.Saved.Last().ReportExportPath.Should().Be(@"D:\Reports");
    }

    [Fact]
    public async Task MovingCard_ReordersAndPersistsOrder()
    {
        _repository.Handler = (_, _) => Task.FromResult(SampleStatistics());
        var sut = CreateSut();
        await sut.LoadAsync();
        var defectCard = sut.MetricCards[1];

        await sut.MoveCardLeftAsync(defectCard);

        sut.MetricCards[0].Should().BeSameAs(defectCard);
        _settings.Saved.Should().ContainSingle();
        _settings.Saved[0].DashboardCardOrder.Should().Equal(
            DashboardCardOrderPolicy.TotalDefects,
            DashboardCardOrderPolicy.TotalPcbs,
            DashboardCardOrderPolicy.PassFail,
            DashboardCardOrderPolicy.Distribution);
    }

    [Fact]
    public async Task LayoutToggle_PersistsSingleColumn()
    {
        _repository.Handler = (_, _) => Task.FromResult(SampleStatistics());
        var sut = CreateSut();
        await sut.LoadAsync();

        sut.SetOneColumnLayoutCommand.Execute(null);
        await WaitUntilAsync(() => _settings.Saved.Count > 0);

        sut.ColumnLayout.Should().Be(DashboardColumnLayout.OneColumn);
        _settings.Saved.Last().DashboardColumnLayout.Should().Be(DashboardColumnLayout.OneColumn);
    }

    [Fact]
    public async Task SkipDashboardSetting_PersistsOnChange()
    {
        var sut = CreateSut();

        sut.SkipDashboardOnStartup = true;
        await WaitUntilAsync(() => _settings.Saved.Count > 0);

        _settings.Saved.Last().SkipDashboardOnStartup.Should().BeTrue();
    }

    [Fact]
    public async Task SkipDashboardSetting_WhenPersistenceFails_RevertsAndNotifies()
    {
        _settings.SaveException = new IOException("disk unavailable");
        var sut = CreateSut();

        sut.SkipDashboardOnStartup = true;
        await WaitUntilAsync(() => _settings.SaveAttempts > 0);
        await WaitUntilAsync(() => !sut.SkipDashboardOnStartup);
        await WaitUntilAsync(() => _notification.LastTitle is not null);

        sut.SkipDashboardOnStartup.Should().BeFalse();
        _notification.LastTitle.Should().Be("Dashboard settings not saved");
    }

    [Fact]
    public void NotificationModalCommands_ToggleOpenState()
    {
        var sut = CreateSut();

        sut.OpenNotificationsCommand.Execute(null);
        sut.IsNotificationModalOpen.Should().BeTrue();

        sut.CloseNotificationsCommand.Execute(null);
        sut.IsNotificationModalOpen.Should().BeFalse();
    }

    [Fact]
    public async Task ExportRequested_CarriesSnapshotOfActiveFilter()
    {
        _repository.Handler = (_, _) => Task.FromResult(SampleStatistics());
        var sut = CreateSut();
        await sut.LoadAsync();
        sut.Filters.SelectedPreset = DateRangePreset.Day;
        sut.Filters.ClassOptions.Single(option => option.Value == DefectClass.Open).IsSelected = true;
        DashboardFilter? exported = null;
        sut.ExportRequested += (_, filter) => exported = filter;

        sut.RequestExportCommand.Execute(null);

        exported.Should().NotBeNull();
        exported!.StartDateUtc.Should().NotBeNull();
        exported.EndDateUtc.Should().NotBeNull();
        exported.DefectClasses.Should().Equal(DefectClass.Open);
    }

    [Fact]
    public async Task ExportRequested_RepeatedClicksRaiseConsistentSnapshots()
    {
        _repository.Handler = (_, _) => Task.FromResult(SampleStatistics());
        var sut = CreateSut();
        await sut.LoadAsync();
        sut.Filters.ClassOptions.Single(option => option.Value == DefectClass.Short).IsSelected = true;
        var snapshots = new List<DashboardFilter>();
        sut.ExportRequested += (_, filter) => snapshots.Add(filter);

        sut.RequestExportCommand.Execute(null);
        sut.RequestExportCommand.Execute(null);

        snapshots.Should().HaveCount(2);
        snapshots[0].DefectClasses.Should().Equal(DefectClass.Short);
        snapshots[1].DefectClasses.Should().Equal(DefectClass.Short);
    }

    [Fact]
    public async Task LoadAsync_WhenRepositoryFails_SetsErrorAndStopsLoading()
    {
        _repository.Handler = (_, _) => throw new InvalidOperationException("boom");
        var sut = CreateSut();

        await sut.LoadAsync();

        sut.ErrorMessage.Should().Be("Failed to load dashboard statistics.");
        sut.IsLoading.Should().BeFalse();
        sut.HasData.Should().BeFalse();
    }

    [Fact]
    public async Task SupersededLoad_DoesNotApplyStaleStatistics()
    {
        var first = new TaskCompletionSource<DashboardStatistics>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<DashboardStatistics>(TaskCreationOptions.RunContinuationsAsynchronously);
        int call = 0;
        _repository.Handler = (_, _) =>
        {
            int current = Interlocked.Increment(ref call);
            return current == 1 ? first.Task : second.Task;
        };

        var sut = CreateSut();
        Task initialLoad = sut.LoadAsync();
        await WaitUntilAsync(() => call >= 1);

        sut.Filters.SelectedPreset = DateRangePreset.Day;
        await WaitUntilAsync(() => call >= 2);

        second.SetResult(SampleStatistics(totalInspections: 99));
        await WaitUntilAsync(() => sut.Statistics?.TotalInspections == 99);
        first.SetResult(SampleStatistics(totalInspections: 1));
        await initialLoad;

        sut.Statistics!.TotalInspections.Should().Be(99);
    }

    private DashboardViewModel CreateSut() => new(
        _repository,
        _settings,
        _progress,
        notificationService: _notification);

    private static DashboardStatistics SampleStatistics(int totalInspections = 4)
    {
        var buckets = new[]
        {
            new DashboardBucket(
                new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Local),
                new DateTime(2026, 11, 1, 23, 59, 59, DateTimeKind.Local),
                "1 Nov",
                totalInspections - 1,
                1,
                totalInspections - 2 > 0 ? totalInspections - 2 : 0,
                1),
            new DashboardBucket(
                new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Local),
                new DateTime(2026, 11, 2, 23, 59, 59, DateTimeKind.Local),
                "2 Nov",
                1,
                1,
                0,
                1)
        };

        var classes = DefectKnowledgeBase.GetAll()
            .OrderBy(info => info.ClassId)
            .Select(info => new DashboardClassCount(
                info.Class,
                info.Abbreviation,
                info.Class == DefectClass.Short ? 1 : info.Class == DefectClass.Open ? 1 : 0))
            .ToArray();

        return new DashboardStatistics(DashboardBucketGranularity.Day, buckets, classes);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200; i++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was not satisfied.");
    }

    private sealed class FakeDashboardRepository : IDashboardRepository
    {
        public List<DashboardFilter> Requests { get; } = new();

        public Func<DashboardFilter, CancellationToken, Task<DashboardStatistics>> Handler { get; set; } =
            (_, _) => Task.FromResult(DashboardStatistics.Empty);

        public Task<DashboardStatistics> GetStatisticsAsync(
            DashboardFilter filter,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(filter);
            return Handler(filter, cancellationToken);
        }
    }

    private sealed class FakeNotificationService : INotificationService
    {
        public string? LastTitle { get; private set; }

        public Task ShowToastAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            LastTitle = title;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUserSettingsService : IUserSettingsService
    {
        public UserSettings Settings { get; set; } = new();

        public List<UserSettings> Saved { get; } = new();

        public Task<UserSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Settings);

        public Exception? SaveException { get; set; }
        public int SaveAttempts { get; private set; }

        public Task SaveSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            if (SaveException is not null)
            {
                throw SaveException;
            }

            Settings = settings;
            Saved.Add(settings);
            return Task.CompletedTask;
        }
    }
}
