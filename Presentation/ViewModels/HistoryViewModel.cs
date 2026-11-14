using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;
using pcb_deftek.Presentation.Models;
using pcb_deftek.Presentation.Services;

namespace pcb_deftek.Presentation.ViewModels;

/// <summary>
/// Orchestrates the history gallery: filter state, paged loading, local-date grouping,
/// thumbnail hydration, selection, and confirmed batch deletion.
/// </summary>
public sealed partial class HistoryViewModel : ViewModelBase
{
    /// <summary>Number of records loaded per page.</summary>
    public const int PageSize = 60;

    private readonly IInspectionRepository _repository;
    private readonly IHistoryThumbnailService _thumbnailService;
    private readonly INotificationService _notificationService;
    private readonly IInspectionDeletionService _deletionService;
    private readonly IConfirmationService _confirmationService;
    private readonly ILogger<HistoryViewModel> _logger;
    private readonly HashSet<Guid> _loadedIds = new();
    private CancellationTokenSource? _loadCts;
    private int _loadVersion;
    private int _currentPage;
    private bool _hasLoadedOnce;

    /// <summary>
    /// Raised after a confirmed deletion with the identifiers removed from the gallery.
    /// </summary>
    public event EventHandler<IReadOnlyList<Guid>>? RecordsDeleted;

    /// <inheritdoc />
    public override string Title => "History";

    /// <summary>Gets the hosted inspection viewer.</summary>
    public InspectionViewerViewModel Viewer { get; }

    /// <summary>Gets the filter bar state.</summary>
    public HistoryFilterViewModel Filters { get; }

    /// <summary>Gets the loaded gallery cards.</summary>
    public ObservableCollection<HistoryItemViewModel> Items { get; } = new();

    /// <summary>Gets the cards grouped by local calendar date.</summary>
    public ObservableCollection<HistoryDateGroup<HistoryItemViewModel>> Groups { get; } = new();

    [ObservableProperty]
    private bool _isViewerOpen;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private bool _isDeleting;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Initializes a new instance of the <see cref="HistoryViewModel"/> class.
    /// </summary>
    public HistoryViewModel(
        IInspectionRepository repository,
        IHistoryThumbnailService thumbnailService,
        INotificationService notificationService,
        IInspectionDeletionService deletionService,
        IConfirmationService confirmationService,
        InspectionViewerViewModel viewer,
        ILogger<HistoryViewModel>? logger = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _thumbnailService = thumbnailService ?? throw new ArgumentNullException(nameof(thumbnailService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _deletionService = deletionService ?? throw new ArgumentNullException(nameof(deletionService));
        _confirmationService = confirmationService ?? throw new ArgumentNullException(nameof(confirmationService));
        Viewer = viewer ?? throw new ArgumentNullException(nameof(viewer));
        _logger = logger ?? NullLogger<HistoryViewModel>.Instance;

        Viewer.Closed += OnViewerClosed;
        Viewer.Deleted += OnViewerRecordDeleted;
        Viewer.Renamed += OnViewerRenamed;
        Viewer.InspectionCreated += OnViewerInspectionCreated;
        Filters = new HistoryFilterViewModel();
        Filters.FiltersChanged += OnFiltersChanged;
    }

    /// <summary>Gets a value indicating whether another page can be loaded.</summary>
    public bool HasMore => Items.Count < TotalCount;

    /// <summary>Gets a value indicating whether Load More can execute.</summary>
    public bool CanLoadMore => !IsLoading && HasMore;

    /// <summary>Gets a value indicating whether there is no history at all.</summary>
    public bool IsEmpty => !IsLoading && _hasLoadedOnce && ErrorMessage is null && Items.Count == 0 && !Filters.HasActiveFilters;

    /// <summary>Gets a value indicating whether the first page is still loading.</summary>
    public bool IsInitialLoading => IsLoading && Items.Count == 0;

    /// <summary>Gets a value indicating whether filters excluded every record.</summary>
    public bool HasNoMatches => !IsLoading && _hasLoadedOnce && ErrorMessage is null && Items.Count == 0 && Filters.HasActiveFilters;

    /// <summary>Gets the number of selected cards.</summary>
    public int SelectedCount => Items.Count(item => item.IsSelected);

    /// <summary>Gets a value indicating whether selected records can be deleted.</summary>
    public bool CanDeleteSelected => SelectedCount > 0 && !IsDeleting;

    /// <summary>Gets the pluralized result count label.</summary>
    public string ResultsSummary => TotalCount == 1 ? "1 result" : $"{TotalCount} results";

    /// <summary>Gets the "showing x of y" footer label.</summary>
    public string ShowingSummary => $"Showing {Items.Count} / {TotalCount}";

    /// <summary>
    /// Opens the viewer for the specified card and loads its full record.
    /// </summary>
    public async Task OpenViewerAsync(HistoryItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        IsViewerOpen = true;
        await Viewer.LoadAsync(item.Id);
    }

    /// <inheritdoc />
    public override Task OnNavigatedToAsync() => LoadFirstPageAsync();

    /// <inheritdoc />
    public override Task OnNavigatedFromAsync()
    {
        _loadCts?.Cancel();
        Viewer.Close();
        IsViewerOpen = false;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Loads the first page using the current filters, replacing the gallery contents.
    /// </summary>
    public async Task LoadFirstPageAsync(CancellationToken cancellationToken = default)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loadCts?.Cancel();
        _loadCts = cts;

        int version = Interlocked.Increment(ref _loadVersion);
        var token = cts.Token;

        foreach (var item in Items)
        {
            item.IsSelected = false;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var page = await _repository
                .GetSummariesPagedAsync(Filters.ToQueryFilter(pageNumber: 1, PageSize), token);

            token.ThrowIfCancellationRequested();
            if (version != _loadVersion)
            {
                return;
            }

            ClearItems();
            _currentPage = 1;
            TotalCount = page.TotalItems;

            foreach (var summary in page.Items)
            {
                AddItem(summary);
            }

            RebuildGroups();
            NotifyGalleryState();
            StartThumbnailHydration(Items.ToList(), token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load or navigation.
        }
        catch (Exception ex)
        {
            if (version == _loadVersion && !token.IsCancellationRequested)
            {
                _logger.LogError(ex, "Failed to load history page 1.");
                ErrorMessage = "Failed to load inspection history.";
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                _hasLoadedOnce = true;
                IsLoading = false;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private async Task LoadMoreAsync()
    {
        if (IsLoading || !HasMore)
        {
            return;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
        _loadCts?.Cancel();
        _loadCts = cts;

        int version = Interlocked.Increment(ref _loadVersion);
        var token = cts.Token;
        IsLoading = true;

        try
        {
            int nextPage = _currentPage + 1;
            var page = await _repository
                .GetSummariesPagedAsync(Filters.ToQueryFilter(nextPage, PageSize), token);

            token.ThrowIfCancellationRequested();
            if (version != _loadVersion)
            {
                return;
            }

            var added = new List<HistoryItemViewModel>();
            foreach (var summary in page.Items)
            {
                if (_loadedIds.Add(summary.Id))
                {
                    added.Add(AddItem(summary));
                }
            }

            _currentPage = nextPage;
            TotalCount = page.TotalItems;
            RebuildGroups();
            NotifyGalleryState();
            StartThumbnailHydration(added, token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load or navigation.
        }
        catch (Exception ex)
        {
            if (version == _loadVersion && !token.IsCancellationRequested)
            {
                _logger.LogError(ex, "Failed to load history page {PageNumber}.", _currentPage + 1);
                ErrorMessage = "Failed to load more inspections.";
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadFirstPageAsync();

    [RelayCommand]
    private void ClearFilters() => Filters.ClearAll();

    [RelayCommand]
    private void SelectLoaded()
    {
        foreach (var item in Items)
        {
            item.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var item in Items)
        {
            item.IsSelected = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
        var selected = Items.Where(item => item.IsSelected).ToList();
        if (selected.Count == 0 || IsDeleting)
        {
            return;
        }

        bool confirmed = await _confirmationService
            .ConfirmAsync(
                "Delete Inspections",
                $"Delete {selected.Count} inspection(s)? App-owned capture images will be deleted. " +
                "Original source images are kept.",
                "Delete",
                CancellationToken.None);

        if (!confirmed)
        {
            return;
        }

        IsDeleting = true;
        try
        {
            var ids = selected.Select(item => item.Id).ToArray();
            var result = await _deletionService
                .DeleteManyAsync(ids, CancellationToken.None);

            bool requiresPageReset = Items.Count < TotalCount;

            foreach (var item in selected)
            {
                RemoveItem(item);
            }

            TotalCount = Math.Max(0, TotalCount - selected.Count);

            if (requiresPageReset)
            {
                await LoadFirstPageAsync();
            }
            else
            {
                RebuildGroups();
                NotifyGalleryState();
            }

            string message = $"Deleted {result.DeletedRecordCount} inspection(s); cleaned {result.DeletedFileCount} capture file(s).";
            if (result.HasFileFailures)
            {
                message += $" {result.FailedFilePaths.Count} file(s) could not be removed.";
            }

            await ShowToastSafeAsync("Deleted Inspections", message);
            if (Viewer.CurrentRecordId is Guid openId && ids.Contains(openId))
            {
                Viewer.Close();
                IsViewerOpen = false;
            }

            RecordsDeleted?.Invoke(this, ids);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete {Count} inspection(s).", selected.Count);
            ErrorMessage = "Failed to delete selected inspections.";
            await ShowToastSafeAsync("Deletion failed", ex.Message);
        }
        finally
        {
            IsDeleting = false;
        }
    }

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanLoadMore));
        LoadMoreCommand.NotifyCanExecuteChanged();
        NotifyGalleryState();
    }

    partial void OnTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(CanLoadMore));
        OnPropertyChanged(nameof(ResultsSummary));
        OnPropertyChanged(nameof(ShowingSummary));
        LoadMoreCommand.NotifyCanExecuteChanged();
    }

    private void OnFiltersChanged(object? sender, EventArgs e)
    {
        if (!Filters.IsCustomRangeValid)
        {
            NotifyGalleryState();
            return;
        }

        _ = ReloadForFilterChangeAsync();
    }

    private async Task ReloadForFilterChangeAsync()
    {
        try
        {
            await LoadFirstPageAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload history after a filter change.");
        }
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HistoryItemViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedCount));
            DeleteSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnViewerClosed(object? sender, EventArgs e)
    {
        IsViewerOpen = false;
    }

    private void OnViewerRenamed(object? sender, InspectionRenamedEventArgs e)
    {
        Items.FirstOrDefault(item => item.Id == e.InspectionId)
            ?.ApplyPersistedName(e.Name);
    }

    private void OnViewerInspectionCreated(object? sender, Guid inspectionId)
    {
        _ = ReloadAfterCropAsync();
    }

    private async Task ReloadAfterCropAsync()
    {
        try
        {
            await LoadFirstPageAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh history after a crop was added.");
        }
    }

    private void OnViewerRecordDeleted(object? sender, Guid recordId)
    {
        var item = Items.FirstOrDefault(candidate => candidate.Id == recordId);
        if (item is not null)
        {
            RemoveItem(item);
            TotalCount = Math.Max(0, TotalCount - 1);
            RebuildGroups();
            NotifyGalleryState();
        }

        IsViewerOpen = false;
    }

    private void OnItemRenameFailed(object? sender, string message)
    {
        _logger.LogWarning("History rename failed: {Message}", message);
        _ = ShowToastSafeAsync("Rename failed", message);
    }

    private HistoryItemViewModel AddItem(InspectionSummary summary)
    {
        var item = new HistoryItemViewModel(summary, _repository);
        item.PropertyChanged += OnItemPropertyChanged;
        item.RenameFailed += OnItemRenameFailed;
        Items.Add(item);
        _loadedIds.Add(summary.Id);
        return item;
    }

    private void RemoveItem(HistoryItemViewModel item)
    {
        item.PropertyChanged -= OnItemPropertyChanged;
        item.RenameFailed -= OnItemRenameFailed;
        Items.Remove(item);
        _loadedIds.Remove(item.Id);
    }

    private void ClearItems()
    {
        foreach (var item in Items)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
            item.RenameFailed -= OnItemRenameFailed;
        }

        Items.Clear();
        _loadedIds.Clear();
        Groups.Clear();
    }

    private void RebuildGroups()
    {
        Groups.Clear();
        foreach (var group in HistoryGrouping.Group(Items, item => item.CreatedAtUtc))
        {
            Groups.Add(group);
        }
    }

    private void NotifyGalleryState()
    {
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(CanLoadMore));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsInitialLoading));
        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(ResultsSummary));
        OnPropertyChanged(nameof(ShowingSummary));
        LoadMoreCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    private void StartThumbnailHydration(IReadOnlyList<HistoryItemViewModel> items, CancellationToken token)
    {
        if (items.Count == 0)
        {
            return;
        }

        _ = HydrateThumbnailsAsync(items, token);
    }

    private async Task HydrateThumbnailsAsync(IReadOnlyList<HistoryItemViewModel> items, CancellationToken token)
    {
        try
        {
            await Task.WhenAll(items.Select(item => item.LoadThumbnailAsync(_thumbnailService, token)));
        }
        catch (OperationCanceledException)
        {
            // Navigation moved on.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "History thumbnail hydration encountered an unexpected error.");
        }
    }

    private async Task ShowToastSafeAsync(string title, string message)
    {
        try
        {
            await _notificationService.ShowToastAsync(title, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch notification '{Title}'.", title);
        }
    }
}
