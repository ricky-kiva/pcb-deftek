using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Presentation.Services;

namespace pcb_deftek.Presentation.ViewModels;

/// <summary>
/// Orchestrates dashboard filter state, aggregate loading, metric-card projections,
/// persisted card order/layout, defect knowledge items, and action seams.
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private const string TotalPcbsColor = "#0066FF";
    private const string TotalDefectsColor = "#E81123";

    private readonly IDashboardRepository _repository;
    private readonly IUserSettingsService _settingsService;
    private readonly ILogger<DashboardViewModel> _logger;
    private readonly SemaphoreSlim _settingsSaveGate = new(1, 1);
    private readonly SynchronizationContext? _uiContext;
    private CancellationTokenSource? _loadCts;
    private int _loadVersion;
    private bool _suppressPreferenceSave;
    private UserSettings _settings = new();
    private List<string> _cardOrder = DashboardCardOrderPolicy.DefaultOrder.ToList();

    /// <summary>
    /// Raised when Export Data is clicked with a valid filter snapshot.
    /// Phase 9 subscribes to generate the PDF report.
    /// </summary>
    public event EventHandler<DashboardFilter>? ExportRequested;

    /// <inheritdoc />
    public override string Title => "Dashboard";

    /// <summary>Gets the dashboard filter state.</summary>
    public DashboardFilterViewModel Filters { get; }

    /// <summary>Gets the background progress service used by the notification modal.</summary>
    public IDashboardProgressService Progress { get; }

    /// <summary>Gets the ordered metric cards.</summary>
    public ObservableCollection<DashboardMetricCardViewModel> MetricCards { get; } = new();

    /// <summary>Gets the nine defect knowledge items in taxonomy order.</summary>
    public IReadOnlyList<DefectInfoItemViewModel> DefectInfoItems { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasData))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasNoMatches))]
    private DashboardStatistics? _statistics;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasNoMatches))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasNoMatches))]
    private string? _errorMessage;

    [ObservableProperty]
    private DashboardColumnLayout _columnLayout = DashboardColumnLayout.TwoColumns;

    [ObservableProperty]
    private bool _skipDashboardOnStartup;

    [ObservableProperty]
    private bool _isNotificationModalOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChartModalOpen))]
    private DashboardMetricCardViewModel? _activeChartCard;

    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardViewModel"/> class.
    /// </summary>
    public DashboardViewModel(
        IDashboardRepository repository,
        IUserSettingsService settingsService,
        IDashboardProgressService progressService,
        ILogger<DashboardViewModel>? logger = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        Progress = progressService ?? throw new ArgumentNullException(nameof(progressService));
        _logger = logger ?? NullLogger<DashboardViewModel>.Instance;
        _uiContext = SynchronizationContext.Current;

        Filters = new DashboardFilterViewModel();
        Filters.FiltersChanged += OnFiltersChanged;
        DefectInfoItems = DefectKnowledgeBase.GetAll()
            .OrderBy(info => info.ClassId)
            .Select(info => new DefectInfoItemViewModel(info))
            .ToArray();
    }

    /// <summary>Gets a value indicating whether the current result has inspections.</summary>
    public bool HasData => Statistics?.HasData == true;

    /// <summary>Gets a value indicating whether the dashboard has no data and no filters.</summary>
    public bool IsEmpty =>
        !IsLoading && ErrorMessage is null && Statistics is { HasData: false } && !Filters.HasActiveFilters;

    /// <summary>Gets a value indicating whether active filters matched no records.</summary>
    public bool HasNoMatches =>
        !IsLoading && ErrorMessage is null && Statistics is { HasData: false } && Filters.HasActiveFilters;

    /// <summary>Gets a value indicating whether the saved layout is two columns.</summary>
    public bool IsTwoColumnLayout => ColumnLayout == DashboardColumnLayout.TwoColumns;

    /// <summary>Gets a value indicating whether the enlarged chart modal is open.</summary>
    public bool IsChartModalOpen => ActiveChartCard is not null;

    /// <inheritdoc />
    public override Task OnNavigatedToAsync() => LoadAsync();

    /// <inheritdoc />
    public override Task OnNavigatedFromAsync()
    {
        _loadCts?.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Loads persisted preferences and the current all-time/filter statistics.
    /// </summary>
    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        LoadCoreAsync(loadPreferences: true, cancellationToken);

    /// <summary>
    /// Reloads statistics with the current filter, cancelling any in-flight request.
    /// </summary>
    public Task ReloadAsync(CancellationToken cancellationToken = default) =>
        LoadCoreAsync(loadPreferences: false, cancellationToken);

    [RelayCommand]
    public async Task MoveCardLeftAsync(DashboardMetricCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        int index = MetricCards.IndexOf(card);
        if (index <= 0)
        {
            return;
        }

        MetricCards.Move(index, index - 1);
        _cardOrder = MetricCards.Select(item => item.Key).ToList();
        await PersistPreferencesAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task MoveCardRightAsync(DashboardMetricCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        int index = MetricCards.IndexOf(card);
        if (index < 0 || index >= MetricCards.Count - 1)
        {
            return;
        }

        MetricCards.Move(index, index + 1);
        _cardOrder = MetricCards.Select(item => item.Key).ToList();
        await PersistPreferencesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Moves a card to a drop target index and persists the resulting order.
    /// </summary>
    public async Task MoveCardToAsync(DashboardMetricCardViewModel? card, int targetIndex)
    {
        if (card is null)
        {
            return;
        }

        int index = MetricCards.IndexOf(card);
        if (index < 0)
        {
            return;
        }

        int boundedTarget = Math.Clamp(targetIndex, 0, MetricCards.Count - 1);
        if (boundedTarget == index)
        {
            return;
        }

        MetricCards.Move(index, boundedTarget);
        _cardOrder = MetricCards.Select(item => item.Key).ToList();
        await PersistPreferencesAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private void ToggleLayout()
    {
        ColumnLayout = ColumnLayout == DashboardColumnLayout.TwoColumns
            ? DashboardColumnLayout.OneColumn
            : DashboardColumnLayout.TwoColumns;
    }

    [RelayCommand]
    private void RequestExport()
    {
        if (Filters.TryCreateFilter(out var filter))
        {
            ExportRequested?.Invoke(this, filter);
        }
    }

    [RelayCommand]
    private void OpenNotifications() => IsNotificationModalOpen = true;

    [RelayCommand]
    private void CloseNotifications() => IsNotificationModalOpen = false;

    [RelayCommand]
    private void OpenChart(DashboardMetricCardViewModel? card)
    {
        if (card is not null)
        {
            ActiveChartCard = card;
        }
    }

    [RelayCommand]
    private void CloseChart() => ActiveChartCard = null;

    partial void OnColumnLayoutChanged(DashboardColumnLayout value)
    {
        OnPropertyChanged(nameof(IsTwoColumnLayout));
        if (!_suppressPreferenceSave)
        {
            _ = PersistPreferencesAsync();
        }
    }

    partial void OnSkipDashboardOnStartupChanged(bool value)
    {
        if (!_suppressPreferenceSave)
        {
            _ = PersistPreferencesAsync();
        }
    }

    private void OnFiltersChanged(object? sender, EventArgs e)
    {
        if (!Filters.TryCreateFilter(out _))
        {
            _loadCts?.Cancel();
            return;
        }

        _ = ReloadAsync();
    }

    private async Task LoadCoreAsync(bool loadPreferences, CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loadCts?.Cancel();
        _loadCts = cts;

        int version = Interlocked.Increment(ref _loadVersion);
        var token = cts.Token;
        RunOnUi(() =>
        {
            IsLoading = true;
            ErrorMessage = null;
        });

        try
        {
            if (loadPreferences)
            {
                UserSettings settings = await _settingsService
                    .LoadSettingsAsync(token);
                token.ThrowIfCancellationRequested();
                if (version != _loadVersion)
                {
                    return;
                }

                RunOnUi(() => ApplyPreferences(settings));
            }

            if (!Filters.TryCreateFilter(out DashboardFilter? filter))
            {
                return;
            }

            DashboardStatistics statistics = await _repository
                .GetStatisticsAsync(filter, token);
            token.ThrowIfCancellationRequested();
            if (version != _loadVersion)
            {
                return;
            }

            RunOnUi(() =>
            {
                Statistics = statistics;
                RebuildMetricCards();
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load or navigation.
        }
        catch (Exception ex)
        {
            if (version == _loadVersion && !token.IsCancellationRequested)
            {
                _logger.LogError(ex, "Failed to load dashboard statistics.");
                RunOnUi(() => ErrorMessage = "Failed to load dashboard statistics.");
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                RunOnUi(() => IsLoading = false);
            }
        }
    }

    private void ApplyPreferences(UserSettings settings)
    {
        _settings = settings ?? new UserSettings();
        _cardOrder = DashboardCardOrderPolicy.Normalize(_settings.DashboardCardOrder).ToList();

        _suppressPreferenceSave = true;
        try
        {
            ColumnLayout = DashboardColumnLayoutPolicy.Normalize(_settings.DashboardColumnLayout);
            SkipDashboardOnStartup = _settings.SkipDashboardOnStartup;
        }
        finally
        {
            _suppressPreferenceSave = false;
        }

        RebuildMetricCards();
    }

    private void RebuildMetricCards()
    {
        DashboardStatistics statistics = Statistics ?? DashboardStatistics.Empty;
        var cards = new List<DashboardMetricCardViewModel>(4);

        foreach (string key in _cardOrder)
        {
            cards.Add(CreateCard(key, statistics));
        }

        MetricCards.Clear();
        foreach (var card in cards)
        {
            MetricCards.Add(card);
        }
    }

    private static DashboardMetricCardViewModel CreateCard(
        string key,
        DashboardStatistics statistics)
    {
        string period = DescribeGranularity(statistics.BucketGranularity);
        string caption = $"{period} · {statistics.Buckets.Count} buckets";
        CultureInfo culture = CultureInfo.CurrentCulture;

        return key switch
        {
            DashboardCardOrderPolicy.TotalPcbs => new DashboardMetricCardViewModel(
                key,
                "Total PCBs Inspected",
                statistics.TotalInspections.ToString("N0", culture),
                caption,
                DashboardMetricCardKind.TimeSeries,
                timeSeries: statistics.Buckets
                    .Select(bucket => new DashboardTimeSeriesPoint(bucket.Label, bucket.Inspections))
                    .ToArray(),
                seriesColorHex: TotalPcbsColor,
                accessibleSummary:
                    $"{statistics.TotalInspections} PCBs scanned across {statistics.Buckets.Count} {period.ToLowerInvariant()} buckets.",
                granularityLabel: period),

            DashboardCardOrderPolicy.TotalDefects => new DashboardMetricCardViewModel(
                key,
                "Total Defects Detected",
                statistics.TotalDefects.ToString("N0", culture),
                caption,
                DashboardMetricCardKind.TimeSeries,
                timeSeries: statistics.Buckets
                    .Select(bucket => new DashboardTimeSeriesPoint(bucket.Label, bucket.Defects))
                    .ToArray(),
                seriesColorHex: TotalDefectsColor,
                accessibleSummary:
                    $"{statistics.TotalDefects} defects detected across {statistics.Buckets.Count} {period.ToLowerInvariant()} buckets.",
                granularityLabel: period),

            DashboardCardOrderPolicy.PassFail => new DashboardMetricCardViewModel(
                key,
                "PASS/FAIL Inspection Rate",
                $"{statistics.PassCount.ToString("N0", culture)} PASS / {statistics.FailCount.ToString("N0", culture)} FAIL",
                caption,
                DashboardMetricCardKind.PassFail,
                passFailSeries: statistics.Buckets
                    .Select(bucket => new DashboardPassFailPoint(bucket.Label, bucket.Passes, bucket.Fails))
                    .ToArray(),
                accessibleSummary:
                    $"{statistics.PassCount} PASS and {statistics.FailCount} FAIL inspections across {statistics.Buckets.Count} {period.ToLowerInvariant()} buckets.",
                granularityLabel: period),

            DashboardCardOrderPolicy.Distribution => new DashboardMetricCardViewModel(
                key,
                "Defect Distribution Across Defect Classes",
                statistics.TotalDefects.ToString("N0", culture),
                "All nine defect classes",
                DashboardMetricCardKind.Distribution,
                classCounts: statistics.ClassCounts,
                accessibleSummary:
                    $"{statistics.TotalDefects} defects distributed across {statistics.ClassCounts.Count} defect classes."),

            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown dashboard card key.")
        };
    }

    private void RunOnUi(Action action)
    {
        if (_uiContext is null || ReferenceEquals(SynchronizationContext.Current, _uiContext))
        {
            action();
            return;
        }

        _uiContext.Send(_ => action(), null);
    }

    private static string DescribeGranularity(DashboardBucketGranularity granularity) => granularity switch
    {
        DashboardBucketGranularity.Hour => "Hourly",
        DashboardBucketGranularity.Day => "Daily",
        DashboardBucketGranularity.Week => "Weekly",
        DashboardBucketGranularity.Month => "Monthly",
        _ => throw new ArgumentOutOfRangeException(nameof(granularity))
    };

    private async Task PersistPreferencesAsync()
    {
        await _settingsSaveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            UserSettings current = await _settingsService.LoadSettingsAsync().ConfigureAwait(false);
            _settings = current with
            {
                DashboardCardOrder = _cardOrder.ToArray(),
                DashboardColumnLayout = ColumnLayout,
                SkipDashboardOnStartup = SkipDashboardOnStartup
            };

            await _settingsService.SaveSettingsAsync(_settings).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist dashboard preferences.");
        }
        finally
        {
            _settingsSaveGate.Release();
        }
    }
}
