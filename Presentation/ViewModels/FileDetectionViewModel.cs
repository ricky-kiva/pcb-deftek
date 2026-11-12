using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Inference;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;

namespace pcb_deftek.Presentation.ViewModels;

/// <summary>
/// View model backing the file-based and batch defect inspection page (Phase 5).
/// Orchestrates file/folder ingestion, queue management, asynchronous batch execution with rolling ETA,
/// cancellation, and Windows toast completion notifications.
/// </summary>
public partial class FileDetectionViewModel : ViewModelBase
{
    private readonly IFileDiscoveryService _fileDiscoveryService;
    private readonly IFilePickerService _filePickerService;
    private readonly IBatchInspectionEngine _batchInspectionEngine;
    private readonly INotificationService _notificationService;
    private readonly IUserSettingsService _userSettingsService;
    private readonly ILogger<FileDetectionViewModel> _logger;

    private CancellationTokenSource? _cancellationTokenSource;
    private bool _cancellationRequested;

    /// <inheritdoc />
    public override string Title => "File-Based Detection";

    /// <summary>
    /// Gets the collection of items currently in the batch queue.
    /// </summary>
    public ObservableCollection<FileDetectionQueueItemViewModel> QueueItems { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItems))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private int _totalCount;

    [ObservableProperty]
    private int _processedCount;

    [ObservableProperty]
    private int _passCount;

    [ObservableProperty]
    private int _failCount;

    [ObservableProperty]
    private int _errorCount;

    [ObservableProperty]
    private int _percentComplete;

    [ObservableProperty]
    private string? _etaText;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    private bool _isProcessing;

    [ObservableProperty]
    private double _confidenceThreshold = 0.25;

    /// <summary>Gets a value indicating whether there are items in the queue.</summary>
    public bool HasItems => QueueItems.Count > 0;

    /// <summary>Gets a value indicating whether batch inspection can be started.</summary>
    public bool CanStart => HasItems && !IsProcessing;

    /// <summary>Gets a value indicating whether active batch inspection can be cancelled.</summary>
    public bool CanCancel => IsProcessing && !_cancellationRequested;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileDetectionViewModel"/> class.
    /// </summary>
    public FileDetectionViewModel(
        IFileDiscoveryService fileDiscoveryService,
        IFilePickerService filePickerService,
        IBatchInspectionEngine batchInspectionEngine,
        INotificationService notificationService,
        IUserSettingsService userSettingsService,
        ILogger<FileDetectionViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fileDiscoveryService);
        ArgumentNullException.ThrowIfNull(filePickerService);
        ArgumentNullException.ThrowIfNull(batchInspectionEngine);
        ArgumentNullException.ThrowIfNull(notificationService);
        ArgumentNullException.ThrowIfNull(userSettingsService);

        _fileDiscoveryService = fileDiscoveryService;
        _filePickerService = filePickerService;
        _batchInspectionEngine = batchInspectionEngine;
        _notificationService = notificationService;
        _userSettingsService = userSettingsService;
        _logger = logger ?? NullLogger<FileDetectionViewModel>.Instance;
    }

    /// <inheritdoc />
    public override async Task OnNavigatedToAsync()
    {
        try
        {
            var settings = await _userSettingsService.LoadSettingsAsync().ConfigureAwait(false);
            ConfidenceThreshold = settings.ConfidenceThreshold;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load user settings into FileDetectionViewModel.");
        }
    }

    /// <summary>
    /// Opens the Windows file dialog to select multiple PCB images.
    /// </summary>
    [RelayCommand]
    public async Task BrowseFilesAsync()
    {
        if (IsProcessing)
        {
            return;
        }

        try
        {
            var pickedPaths = await _filePickerService.PickFilesAsync(
                title: "Select PCB Images for Batch Inspection",
                multiselect: true).ConfigureAwait(false);

            if (pickedPaths.Count == 0)
            {
                return;
            }

            var discovered = await _fileDiscoveryService.DiscoverImageFilesAsync(pickedPaths).ConfigureAwait(false);
            AddPathsToQueue(discovered);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during BrowseFilesAsync.");
            StatusText = $"Error browsing files: {ex.Message}";
        }
    }

    /// <summary>
    /// Opens the Windows folder dialog to ingest an entire folder of PCB images recursively.
    /// </summary>
    [RelayCommand]
    public async Task BrowseFolderAsync()
    {
        if (IsProcessing)
        {
            return;
        }

        try
        {
            var folder = await _filePickerService.PickFolderAsync(
                title: "Select PCB Image Batch Folder").ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            StatusText = "Scanning directory for PCB images...";
            var discovered = await _fileDiscoveryService.DiscoverImageFilesAsync(folder).ConfigureAwait(false);
            AddPathsToQueue(discovered);
            StatusText = $"Discovered {discovered.Count} image(s) in folder.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during BrowseFolderAsync.");
            StatusText = $"Error browsing folder: {ex.Message}";
        }
    }

    /// <summary>
    /// Ingests dropped files or directory paths from drag-and-drop actions.
    /// </summary>
    [RelayCommand]
    public async Task DropFilesAsync(IEnumerable<string>? paths)
    {
        if (IsProcessing || paths == null)
        {
            return;
        }

        try
        {
            var discovered = await _fileDiscoveryService.DiscoverImageFilesAsync(paths).ConfigureAwait(false);
            AddPathsToQueue(discovered);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during DropFilesAsync.");
            StatusText = $"Error dropping files: {ex.Message}";
        }
    }

    /// <summary>
    /// Clears all items from the queue when idle.
    /// </summary>
    [RelayCommand]
    public void ClearQueue()
    {
        if (IsProcessing)
        {
            return;
        }

        QueueItems.Clear();
        TotalCount = 0;
        ProcessedCount = 0;
        PassCount = 0;
        FailCount = 0;
        ErrorCount = 0;
        PercentComplete = 0;
        EtaText = null;
        StatusText = "Queue cleared.";
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(CanStart));
    }

    /// <summary>
    /// Starts asynchronous batch defect inspection across all queued items.
    /// </summary>
    [RelayCommand]
    public async Task StartBatchAsync()
    {
        if (!CanStart)
        {
            return;
        }

        IsProcessing = true;
        _cancellationRequested = false;
        _cancellationTokenSource = new CancellationTokenSource();

        ProcessedCount = 0;
        PassCount = 0;
        FailCount = 0;
        ErrorCount = 0;
        PercentComplete = 0;
        EtaText = null;
        StatusText = "Starting batch inspection...";

        foreach (var item in QueueItems)
        {
            item.Status = BatchItemStatus.Pending;
        }

        var lookup = QueueItems.ToDictionary(i => i.FilePath, StringComparer.OrdinalIgnoreCase);
        var filePaths = QueueItems.Select(i => i.FilePath).ToList();

        var progress = new Progress<BatchProgressReport>(report =>
        {
            if (!IsProcessing)
            {
                return;
            }

            ProcessedCount = Math.Max(ProcessedCount, report.ProcessedCount);
            PassCount = Math.Max(PassCount, report.PassCount);
            FailCount = Math.Max(FailCount, report.FailCount);
            ErrorCount = Math.Max(ErrorCount, report.ErrorCount);
            PercentComplete = Math.Max(PercentComplete, report.PercentComplete);

            if (report.EstimatedTimeRemaining.HasValue)
            {
                EtaText = $"ETA: {report.EstimatedTimeRemaining.Value:mm\\:ss}";
            }
            else if (report.ProcessedCount < report.TotalCount && report.ProcessedCount > 0)
            {
                EtaText = "ETA: Calculating...";
            }
            else
            {
                EtaText = null;
            }

            if (!string.IsNullOrWhiteSpace(report.CurrentFileName) && report.ProcessedCount < report.TotalCount)
            {
                StatusText = $"Inspecting {report.ProcessedCount + 1} of {report.TotalCount} ({report.CurrentFileName})...";
            }

            if (report.LatestCompletedItem != null && lookup.TryGetValue(report.LatestCompletedItem.FilePath, out var queueItem))
            {
                queueItem.UpdateFrom(report.LatestCompletedItem);
            }
        });

        try
        {
            var summary = await _batchInspectionEngine.ExecuteBatchAsync(
                filePaths,
                progress,
                (float)ConfidenceThreshold,
                _cancellationTokenSource.Token).ConfigureAwait(false);
            IsProcessing = false;

            ProcessedCount = summary.ProcessedCount;
            PassCount = summary.PassCount;
            FailCount = summary.FailCount;
            ErrorCount = summary.ErrorCount;
            PercentComplete = summary.TotalCount > 0
                ? (int)Math.Clamp(Math.Round((double)summary.ProcessedCount / summary.TotalCount * 100.0), 0, 100)
                : 0;

            // Ensure all completed items reflect final summary state
            foreach (var completedItem in summary.Items)
            {
                if (lookup.TryGetValue(completedItem.FilePath, out var queueItem))
                {
                    queueItem.UpdateFrom(completedItem);
                }
            }

            if (summary.Status == BatchStatus.Cancelled)
            {
                StatusText = $"Cancelled: {summary.ProcessedCount} of {summary.TotalCount} processed.";
            }
            else
            {
                StatusText = $"Completed: {summary.PassCount} passed, {summary.FailCount} failed, {summary.ErrorCount} errors.";

                try
                {
                    await _notificationService.ShowToastAsync(
                        "Batch Inspection Complete",
                        $"Processed {summary.TotalCount} boards: {summary.PassCount} passed, {summary.FailCount} failed in {summary.TotalDuration:mm\\:ss}.").ConfigureAwait(false);
                }
                catch (Exception notifEx)
                {
                    _logger.LogWarning(notifEx, "Failed to send batch completion desktop toast notification.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Batch processing cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected exception during batch inspection execution.");
            StatusText = $"Batch inspection error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            _cancellationRequested = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            EtaText = null;
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanCancel));
        }
    }

    /// <summary>
    /// Cancels active batch defect inspection (wired to the inline Cancel button next to the ETA).
    /// </summary>
    [RelayCommand]
    public void CancelBatch()
    {
        if (!IsProcessing || _cancellationTokenSource == null || _cancellationRequested)
        {
            return;
        }

        _cancellationRequested = true;
        OnPropertyChanged(nameof(CanCancel));
        StatusText = "Cancelling processing remaining boards...";

        try
        {
            _cancellationTokenSource.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while signalling batch cancellation.");
        }
    }

    private void AddPathsToQueue(IEnumerable<string> paths)
    {
        var existing = new HashSet<string>(QueueItems.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path) && !existing.Contains(path))
            {
                existing.Add(path);
                QueueItems.Add(new FileDetectionQueueItemViewModel(path));
            }
        }

        TotalCount = QueueItems.Count;
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(CanStart));
    }
}
