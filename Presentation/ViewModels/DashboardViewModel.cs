using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
using pcb_deftek.Infrastructure.Export;
using pcb_deftek.Infrastructure.Notifications;
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

    /// <summary>Number of matching inspections above which the operator must confirm an export.</summary>
    public const int LargeExportInspectionThreshold = 100;

    private readonly IDashboardRepository _repository;
    private readonly IUserSettingsService _settingsService;
    private readonly IDashboardExportService _exportService;
    private readonly IConfirmationService _confirmationService;
    private readonly ILogger<DashboardViewModel> _logger;
    private readonly INotificationService? _notificationService;
    private readonly SemaphoreSlim _settingsSaveGate = new(1, 1);
    private readonly SynchronizationContext? _uiContext;
    private CancellationTokenSource? _loadCts;
    private int _loadVersion;
    private bool _suppressPreferenceSave;
    private UserSettings _settings = new();
    private List<string> _cardOrder = DashboardCardOrderPolicy.DefaultOrder.ToList();

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
    [NotifyCanExecuteChangedFor(nameof(RequestExportCommand))]
    private bool _isExporting;

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
        IDashboardExportService exportService,
        IConfirmationService confirmationService,
        ILogger<DashboardViewModel>? logger = null,
        INotificationService? notificationService = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        Progress = progressService ?? throw new ArgumentNullException(nameof(progressService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _confirmationService = confirmationService ?? throw new ArgumentNullException(nameof(confirmationService));
        _logger = logger ?? NullLogger<DashboardViewModel>.Instance;
        _notificationService = notificationService;
        _uiContext = SynchronizationContext.Current;

        PropertyChangedEventManager.AddHandler(
            Progress,
            OnProgressPropertyChanged,
            nameof(IDashboardProgressService.HasActiveProcesses));

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

    /// <summary>Gets a value indicating whether the saved layout is one column.</summary>
    public bool IsOneColumnLayout => ColumnLayout == DashboardColumnLayout.OneColumn;

    /// <summary>Gets a value indicating whether the enlarged chart modal is open.</summary>
    public bool IsChartModalOpen => ActiveChartCard is not null;

    /// <summary>Gets a value indicating whether at least one background process is running.</summary>
    public bool HasActiveProcesses => Progress.HasActiveProcesses;

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
    private void SetOneColumnLayout() => ColumnLayout = DashboardColumnLayout.OneColumn;

    [RelayCommand]
    private void SetTwoColumnLayout() => ColumnLayout = DashboardColumnLayout.TwoColumns;

    private bool CanRequestExport => !IsExporting;

    [RelayCommand(CanExecute = nameof(CanRequestExport))]
    private async Task RequestExportAsync()
    {
        if (IsExporting || !Filters.TryCreateFilter(out DashboardFilter? filter))
        {
            return;
        }

        IsExporting = true;
        bool exportStarted = false;

        try
        {
            int matchingCount = await _exportService
                .CountMatchingInspectionsAsync(filter);

            if (matchingCount == 0)
            {
                await ShowToastSafeAsync(
                    "No matching inspections",
                    "No inspections match the active dashboard filter.");
                return;
            }

            if (matchingCount > LargeExportInspectionThreshold)
            {
                bool confirmed = await _confirmationService.ConfirmAsync(
                    "Export large report?",
                    $"{matchingCount.ToString("N0", CultureInfo.CurrentCulture)} inspections match the active filter. " +
                    "Generating this report may take a while. Continue?",
                    "Export");
                if (!confirmed)
                {
                    return;
                }
            }

            IsNotificationModalOpen = true;
            ReportExportProgress(DashboardProgressStatus.Running, 0, "Preparing report");
            CancellationToken token = Progress.GetCancellationToken(DashboardProcess.DataExport);
            var progress = new CallbackProgress<DashboardExportProgress>(
                value => ReportExportProgress(
                    DashboardProgressStatus.Running,
                    value.Percentage,
                    value.Message));

            exportStarted = true;
            DashboardExportResult result = await _exportService
                .ExportAsync(filter, progress, token);

            if (result.Outcome == DashboardExportOutcome.NoMatches)
            {
                ReportExportProgress(DashboardProgressStatus.Completed, 100, "No matching inspections");
                await ShowToastSafeAsync(
                    "No matching inspections",
                    "No inspections match the active dashboard filter.");
                return;
            }

            ReportExportProgress(DashboardProgressStatus.Completed, 100, "Report saved");
            await ReloadAsync();
        }
        catch (OperationCanceledException)
        {
            ReportExportProgress(DashboardProgressStatus.Cancelled, 0, "Cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard report export failed.");
            ReportExportProgress(DashboardProgressStatus.Failed, 100, "Export failed");
            if (!exportStarted)
            {
                await ShowToastSafeAsync("Report export failed", ex.Message);
            }
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    private void OpenNotifications() => IsNotificationModalOpen = true;

    [RelayCommand]
    private void CloseNotifications() => IsNotificationModalOpen = false;

    [RelayCommand]
    private void CancelProcess(DashboardProcess process) => Progress.Cancel(process);

    private void OnProgressPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(IDashboardProgressService.HasActiveProcesses))
        {
            OnPropertyChanged(nameof(HasActiveProcesses));
        }
    }

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

    [RelayCommand]
    private void ClearFilters() => Filters.ClearAll();

    [RelayCommand]
    private Task RetryAsync() => LoadAsync();

    partial void OnColumnLayoutChanged(DashboardColumnLayout value)
    {
        OnPropertyChanged(nameof(IsTwoColumnLayout));
        OnPropertyChanged(nameof(IsOneColumnLayout));
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
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasNoMatches));

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
                "PCBs Inspected",
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
                "Defects Detected",
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
                "Inspection Rate",
                $"{statistics.PassCount.ToString("N0", culture)} PASS / {statistics.FailCount.ToString("N0", culture)} FAIL",
                caption,
                DashboardMetricCardKind.PassFail,
                passFailSeries: statistics.Buckets
                    .Select(bucket => new DashboardPassFailPoint(bucket.Label, bucket.Passes, bucket.Fails))
                    .ToArray(),
                accessibleSummary:
                    $"{statistics.PassCount} PASS and {statistics.FailCount} FAIL inspections across {statistics.Buckets.Count} {period.ToLowerInvariant()} buckets.",
                granularityLabel: period,
                passCount: statistics.PassCount,
                failCount: statistics.FailCount),

            DashboardCardOrderPolicy.Distribution => new DashboardMetricCardViewModel(
                key,
                "Defect Distribution",
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

    private void ReportExportProgress(
        DashboardProgressStatus status,
        double percentage,
        string? message)
    {
        try
        {
            Progress.ReportDataExport(status, percentage, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to report data-export progress.");
        }
    }

    private async Task ShowToastSafeAsync(string title, string message)
    {
        if (_notificationService is null)
        {
            return;
        }

        try
        {
            await _notificationService.ShowToastAsync(title, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch notification '{Title}'.", title);
        }
    }

    private sealed class CallbackProgress<T> : IProgress<T>
    {
        private readonly Action<T> _callback;

        public CallbackProgress(Action<T> callback) => _callback = callback;

        public void Report(T value) => _callback(value);
    }

    private async Task PersistPreferencesAsync()
    {
        await _settingsSaveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            UserSettings current = await _settingsService.LoadSettingsAsync().ConfigureAwait(false);
            UserSettings next = current with
            {
                DashboardCardOrder = _cardOrder.ToArray(),
                DashboardColumnLayout = ColumnLayout,
                SkipDashboardOnStartup = SkipDashboardOnStartup
            };

            await _settingsService.SaveSettingsAsync(next).ConfigureAwait(false);
            _settings = next;
        }
        catch (Exception ex)
        {
            UserSettings persisted = _settings;
            try
            {
                persisted = await _settingsService.LoadSettingsAsync().ConfigureAwait(false);
            }
            catch (Exception reloadException)
            {
                _logger.LogWarning(reloadException, "Failed to re-read dashboard settings after a save failure.");
            }

            _settings = persisted;
            bool revertStartup = SkipDashboardOnStartup != persisted.SkipDashboardOnStartup;
            if (revertStartup)
            {
                RunOnUi(() =>
                {
                    _suppressPreferenceSave = true;
                    try
                    {
                        SkipDashboardOnStartup = persisted.SkipDashboardOnStartup;
                    }
                    finally
                    {
                        _suppressPreferenceSave = false;
                    }
                });
            }

            _logger.LogWarning(ex, "Failed to persist dashboard preferences.");
            if (_notificationService is not null)
            {
                _ = _notificationService.ShowToastAsync(
                    "Dashboard settings not saved",
                    revertStartup
                        ? "The startup preference could not be saved. The previous value was restored."
                        : "Card layout changes could not be saved.");
            }
        }
        finally
        {
            _settingsSaveGate.Release();
        }
    }
}
