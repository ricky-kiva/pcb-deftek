using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Notifications;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;
using pcb_deftek.Presentation.Models;
using pcb_deftek.Presentation.Services;

namespace pcb_deftek.Presentation.ViewModels;

/// <summary>
/// Drives the interactive defect viewer: record loading, marker selection, the two sidebar
/// states, inline rename, viewport crop, and confirmed sidebar deletion.
/// </summary>
public sealed partial class InspectionViewerViewModel : ObservableObject
{
    private readonly IInspectionRepository _repository;
    private readonly IViewportCropService _cropService;
    private readonly IInspectionDeletionService _deletionService;
    private readonly IConfirmationService _confirmationService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<InspectionViewerViewModel> _logger;

    private CancellationTokenSource? _loadCts;
    private int _loadVersion;
    private InspectionRecord? _record;
    private InspectionNameEditor? _nameEditor;

    /// <summary>
    /// Raised after the current inspection has been deleted and the viewer state cleared.
    /// </summary>
    public event EventHandler<Guid>? Deleted;

    /// <summary>
    /// Raised whenever the viewer closes and clears its state.
    /// </summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Raised after a crop is written and inserted into the history repository.
    /// </summary>
    public event EventHandler<Guid>? InspectionCreated;

    /// <summary>
    /// Raised after an inspection rename has been persisted.
    /// </summary>
    public event EventHandler<InspectionRenamedEventArgs>? Renamed;

    /// <summary>
    /// Initializes a new instance of the <see cref="InspectionViewerViewModel"/> class.
    /// </summary>
    public InspectionViewerViewModel(
        IInspectionRepository repository,
        IViewportCropService cropService,
        IInspectionDeletionService deletionService,
        IConfirmationService confirmationService,
        INotificationService notificationService,
        ILogger<InspectionViewerViewModel>? logger = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _cropService = cropService ?? throw new ArgumentNullException(nameof(cropService));
        _deletionService = deletionService ?? throw new ArgumentNullException(nameof(deletionService));
        _confirmationService = confirmationService ?? throw new ArgumentNullException(nameof(confirmationService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _logger = logger ?? NullLogger<InspectionViewerViewModel>.Instance;
    }

    /// <summary>Gets the overlay bounding boxes for the loaded record.</summary>
    public ObservableCollection<DefectMarkerViewModel> DefectMarkers { get; } = new();

    /// <summary>Gets the default sidebar class summaries.</summary>
    public ObservableCollection<DefectClassSummaryViewModel> ClassSummaries { get; } = new();

    /// <summary>Gets the persisted record name, or an empty string when no record is loaded.</summary>
    public string CurrentName => _nameEditor?.Name ?? string.Empty;

    /// <summary>Gets the loaded inspection identifier, or null when the viewer is closed.</summary>
    public Guid? CurrentRecordId => _record?.Id;

    /// <summary>Gets a value indicating whether a full-size image is available.</summary>
    public bool HasImage => ImageSource is not null;

    /// <summary>Gets a value indicating whether the record contains any defects.</summary>
    public bool HasDefects => ClassSummaries.Count > 0;

    /// <summary>Gets a value indicating whether the record has no defects.</summary>
    public bool HasNoDefects => !HasDefects;

    /// <summary>Gets the pluralized class-summary count label.</summary>
    public string ClassCountText => ClassSummaries.Count == 1
        ? "1 class"
        : $"{ClassSummaries.Count} classes";

    /// <summary>Gets the focused defect palette color as <c>#RRGGBB</c>.</summary>
    public string FocusedColorHex => SelectedMarker?.ColorHex ?? "#C42B1C";

    /// <summary>Gets the focused class abbreviation.</summary>
    public string FocusedAbbreviation => SelectedMarker?.Abbreviation ?? string.Empty;

    /// <summary>Gets or sets the in-progress name while editing.</summary>
    public string EditedName
    {
        get => _nameEditor?.EditedName ?? string.Empty;
        set
        {
            if (_nameEditor is not null)
            {
                _nameEditor.EditedName = value;
            }
        }
    }

    /// <summary>Gets a value indicating whether the viewer name is being edited.</summary>
    public bool IsEditingName => _nameEditor?.IsEditing ?? false;

    /// <summary>Gets a value indicating whether a rename is being persisted.</summary>
    public bool IsSavingName => _nameEditor?.IsSaving ?? false;

    /// <summary>Gets the local detection timestamp, or an empty string when no record is loaded.</summary>
    public string FormattedTimestamp => _record is null
        ? string.Empty
        : _record.CreatedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    /// <summary>Gets a value indicating whether a marker is focused.</summary>
    public bool IsDefectFocused => SelectedMarker is not null;

    /// <summary>Gets the focused class display name.</summary>
    public string FocusedClassName => SelectedMarker?.ClassName ?? string.Empty;

    /// <summary>Gets the focused confidence formatted as a percentage.</summary>
    public string FocusedConfidenceText => SelectedMarker?.ConfidenceText ?? string.Empty;

    /// <summary>Gets the focused severity label.</summary>
    public string FocusedSeverityText =>
        SelectedMarker is null ? string.Empty : FormatSeverity(SelectedMarker.ClassInfo.Severity);

    /// <summary>Gets the focused root cause.</summary>
    public string FocusedRootCause => SelectedMarker?.ClassInfo.RootCause ?? string.Empty;

    /// <summary>Gets the focused impact.</summary>
    public string FocusedImpact => SelectedMarker?.ClassInfo.Impact ?? string.Empty;

    /// <summary>Gets the focused recommended action.</summary>
    public string FocusedAction => SelectedMarker?.ClassInfo.Action ?? string.Empty;

    /// <summary>Gets a value indicating whether the visible viewport can be cropped.</summary>
    public bool CanCrop => _record is not null && ImageSource is not null && !IsLoading;

    /// <summary>Gets a value indicating whether the current record can be deleted.</summary>
    public bool CanDeleteImage =>
        _record is not null && ImageSource is not null && !IsLoading && !IsDeleting;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CropViewportCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteImageCommand))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteImageCommand))]
    private bool _isDeleting;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CropViewportCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteImageCommand))]
    private BitmapSource? _imageSource;

    [ObservableProperty]
    private bool _isImageUnavailable;

    [ObservableProperty]
    private DefectMarkerViewModel? _selectedMarker;

    [ObservableProperty]
    private string? _errorMessage;

    partial void OnSelectedMarkerChanged(DefectMarkerViewModel? value)
    {
        foreach (var marker in DefectMarkers)
        {
            marker.IsSelected = ReferenceEquals(marker, value);
        }

        OnPropertyChanged(nameof(IsDefectFocused));
        OnPropertyChanged(nameof(FocusedClassName));
        OnPropertyChanged(nameof(FocusedConfidenceText));
        OnPropertyChanged(nameof(FocusedSeverityText));
        OnPropertyChanged(nameof(FocusedRootCause));
        OnPropertyChanged(nameof(FocusedImpact));
        OnPropertyChanged(nameof(FocusedAction));
        OnPropertyChanged(nameof(FocusedColorHex));
        OnPropertyChanged(nameof(FocusedAbbreviation));
    }

    partial void OnImageSourceChanged(BitmapSource? value)
    {
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(CanCrop));
        OnPropertyChanged(nameof(CanDeleteImage));
        CropViewportCommand.NotifyCanExecuteChanged();
        DeleteImageCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCrop));
        OnPropertyChanged(nameof(CanDeleteImage));
    }

    partial void OnIsDeletingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanDeleteImage));
    }

    /// <summary>
    /// Loads the record and its full-size image, rebuilding markers and sidebar summaries.
    /// </summary>
    public async Task LoadAsync(Guid inspectionId, CancellationToken cancellationToken = default)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previous = _loadCts;
        _loadCts = cts;
        previous?.Cancel();

        int version = Interlocked.Increment(ref _loadVersion);
        ResetViewerState();
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var record = await _repository.GetByIdAsync(inspectionId, cts.Token);
            if (version != Volatile.Read(ref _loadVersion))
            {
                return;
            }

            if (record is null)
            {
                IsImageUnavailable = true;
                ErrorMessage = "Inspection not found.";
                return;
            }

            var image = await Task.Run(() => DecodeImage(record.ImagePath), cts.Token);
            if (version != Volatile.Read(ref _loadVersion))
            {
                return;
            }

            ApplyRecord(record, image);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // A newer load or close superseded this one.
        }
        catch (Exception ex)
        {
            if (version == Volatile.Read(ref _loadVersion))
            {
                _logger.LogError(ex, "Failed to load inspection {InspectionId} into the viewer.", inspectionId);
                ErrorMessage = "Failed to load the inspection.";
                IsImageUnavailable = true;
            }
        }
        finally
        {
            if (version == Volatile.Read(ref _loadVersion))
            {
                IsLoading = false;
            }

            if (ReferenceEquals(_loadCts, cts))
            {
                _loadCts = null;
            }
        }
    }

    /// <summary>
    /// Selects a marker for the focused sidebar, or clears focus when null.
    /// </summary>
    public void SelectMarker(DefectMarkerViewModel? marker)
    {
        SelectedMarker = marker;
    }

    /// <summary>
    /// Cancels any in-flight load and clears the viewer.
    /// </summary>
    [RelayCommand]
    public void Close()
    {
        _loadCts?.Cancel();
        _loadCts = null;
        Interlocked.Increment(ref _loadVersion);
        IsLoading = false;
        IsDeleting = false;
        ResetViewerState();
        NotifyRecordState();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Starts inline rename for the loaded record.
    /// </summary>
    [RelayCommand]
    public void BeginRename() => _nameEditor?.BeginRename();

    /// <summary>
    /// Cancels inline rename and restores the persisted name.
    /// </summary>
    [RelayCommand]
    public void CancelRename() => _nameEditor?.CancelRename();

    /// <summary>
    /// Persists the edited name.
    /// </summary>
    [RelayCommand]
    public Task CommitRenameAsync() =>
        _nameEditor?.CommitRenameAsync() ?? Task.FromResult(false);

    /// <summary>
    /// Crops the supplied visible viewport region and reports the exported path.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCrop))]
    private async Task CropViewportAsync(NormalizedRect? visibleRegion)
    {
        var record = _record;
        if (record is null || visibleRegion is null)
        {
            return;
        }

        try
        {
            ViewportCropResult crop = await _cropService.CropAsync(
                record.ImagePath,
                record.Name,
                visibleRegion.Value,
                destinationDirectory: null,
                CancellationToken.None);

            try
            {
                var created = await CreateHistoryRecordAsync(record, crop, CancellationToken.None);
                await ShowToastSafeAsync("Crop saved", $"{crop.FilePath} was added to History.");
                InspectionCreated?.Invoke(this, created.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Crop was written but could not be added to history for inspection {InspectionId}.", record.Id);
                await ShowToastSafeAsync("Crop saved", $"{crop.FilePath} could not be added to History.");
            }
        }
        catch (OperationCanceledException)
        {
            // The crop was cancelled; no user-facing failure is needed.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to crop the visible viewport for inspection {InspectionId}.", record.Id);
            await ShowToastSafeAsync("Crop failed", ex.Message);
        }
    }

    /// <summary>
    /// Confirms and deletes the loaded inspection and its app-owned captures.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeleteImage))]
    private async Task DeleteImageAsync()
    {
        var record = _record;
        if (record is null || ImageSource is null || IsDeleting)
        {
            return;
        }

        bool confirmed = await _confirmationService.ConfirmAsync(
            "Delete Inspection",
            "Delete this inspection and its app-owned cropped/captured images? Original source images are kept.",
            "Delete",
            CancellationToken.None);

        if (!confirmed)
        {
            return;
        }

        IsDeleting = true;
        try
        {
            var result = await _deletionService.DeleteAsync(record.Id, CancellationToken.None);
            string message =
                $"Deleted {result.DeletedRecordCount} inspection(s); cleaned {result.DeletedFileCount} capture file(s).";
            if (result.HasFileFailures)
            {
                message += $" {result.FailedFilePaths.Count} file(s) could not be removed.";
            }

            await ShowToastSafeAsync("Deleted Inspection", message);
            Close();
            Deleted?.Invoke(this, record.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete inspection {InspectionId} from the viewer.", record.Id);
            ErrorMessage = "Failed to delete the inspection.";
            await ShowToastSafeAsync("Deletion failed", ex.Message);
        }
        finally
        {
            IsDeleting = false;
        }
    }

    private async Task<InspectionRecord> CreateHistoryRecordAsync(
        InspectionRecord source,
        ViewportCropResult crop,
        CancellationToken cancellationToken)
    {
        var newId = Guid.NewGuid();
        var region = crop.SourceRegion;
        var defects = new List<DetectedDefect>();

        foreach (var defect in source.Defects)
        {
            var box = defect.BoundingBox;
            float left = Math.Max(box.X, (float)region.X);
            float top = Math.Max(box.Y, (float)region.Y);
            float right = Math.Min(box.Right, (float)region.Right);
            float bottom = Math.Min(box.Bottom, (float)region.Bottom);

            if (right <= left || bottom <= top)
            {
                continue;
            }

            var croppedBounds = BoundingBox.Clamp(
                (float)((left - region.X) / region.Width),
                (float)((top - region.Y) / region.Height),
                (float)((right - left) / region.Width),
                (float)((bottom - top) / region.Height));

            defects.Add(new DetectedDefect(
                Guid.NewGuid(),
                newId,
                defect.ClassCode,
                defect.Confidence,
                croppedBounds));
        }

        var now = DateTime.UtcNow;
        var record = new InspectionRecord(
            newId,
            $"{source.Name} crop",
            now,
            now,
            DetectionMode.SingleFile,
            crop.FilePath,
            defects,
            origin: InspectionOrigin.Crop);

        await _repository.InsertAsync(record, cancellationToken);
        return record;
    }

    private void ApplyRecord(InspectionRecord record, BitmapSource? image)
    {
        _record = record;
        _nameEditor = new InspectionNameEditor(record.Id, record.Name, _repository);
        _nameEditor.PropertyChanged += OnNameEditorPropertyChanged;
        _nameEditor.RenameFailed += OnRenameFailed;

        DefectMarkers.Clear();
        foreach (var defect in record.Defects)
        {
            var bounds = defect.BoundingBox;
            var pixelBounds = image is null
                ? Rect.Empty
                : new Rect(
                    bounds.X * image.PixelWidth,
                    bounds.Y * image.PixelHeight,
                    bounds.Width * image.PixelWidth,
                    bounds.Height * image.PixelHeight);

            DefectMarkers.Add(new DefectMarkerViewModel(defect, pixelBounds));
        }

        ClassSummaries.Clear();
        foreach (var group in record.Defects
                     .GroupBy(defect => defect.Class)
                     .OrderBy(group => (int)group.Key))
        {
            ClassSummaries.Add(new DefectClassSummaryViewModel(
                DefectKnowledgeBase.GetInfo(group.Key),
                group.Count(),
                group.Max(defect => defect.Confidence)));
        }

        ImageSource = image;
        IsImageUnavailable = image is null;
        OnPropertyChanged(nameof(HasDefects));
        OnPropertyChanged(nameof(HasNoDefects));
        NotifyRecordState();
    }

    private void ResetViewerState()
    {
        if (_nameEditor is not null)
        {
            _nameEditor.PropertyChanged -= OnNameEditorPropertyChanged;
            _nameEditor.RenameFailed -= OnRenameFailed;
            _nameEditor = null;
        }

        _record = null;
        DefectMarkers.Clear();
        ClassSummaries.Clear();
        SelectedMarker = null;
        ImageSource = null;
        IsImageUnavailable = false;
        ErrorMessage = null;
        NotifyRecordState();
    }

    private void NotifyRecordState()
    {
        OnPropertyChanged(nameof(CurrentName));
        OnPropertyChanged(nameof(CurrentRecordId));
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(HasDefects));
        OnPropertyChanged(nameof(HasNoDefects));
        OnPropertyChanged(nameof(ClassCountText));
        OnPropertyChanged(nameof(EditedName));
        OnPropertyChanged(nameof(IsEditingName));
        OnPropertyChanged(nameof(IsSavingName));
        OnPropertyChanged(nameof(FormattedTimestamp));
        OnPropertyChanged(nameof(CanCrop));
        OnPropertyChanged(nameof(CanDeleteImage));
        CropViewportCommand.NotifyCanExecuteChanged();
        DeleteImageCommand.NotifyCanExecuteChanged();
    }

    private void OnNameEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(InspectionNameEditor.Name):
                OnPropertyChanged(nameof(CurrentName));
                if (_record is not null && _nameEditor is not null)
                {
                    Renamed?.Invoke(this, new InspectionRenamedEventArgs(_record.Id, _nameEditor.Name));
                }

                break;
            case nameof(InspectionNameEditor.EditedName):
                OnPropertyChanged(nameof(EditedName));
                break;
            case nameof(InspectionNameEditor.IsEditing):
                OnPropertyChanged(nameof(IsEditingName));
                break;
            case nameof(InspectionNameEditor.IsSaving):
                OnPropertyChanged(nameof(IsSavingName));
                break;
        }
    }

    private void OnRenameFailed(object? sender, string message)
    {
        _logger.LogWarning("Viewer rename failed: {Message}", message);
        _ = ShowToastSafeAsync("Rename failed", message);
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

    private BitmapSource? DecodeImage(string imagePath)
    {
        try
        {
            if (!File.Exists(imagePath))
            {
                return null;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(Path.GetFullPath(imagePath), UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException
            or NotSupportedException
            or UnauthorizedAccessException
            or ArgumentException
            or System.Runtime.InteropServices.COMException)
        {
            _logger.LogWarning(ex, "Failed to decode viewer image '{ImagePath}'.", imagePath);
            return null;
        }
    }

    private static string FormatSeverity(DefectSeverity severity) => severity switch
    {
        DefectSeverity.LowMedium => "Low-Medium",
        DefectSeverity.MediumHigh => "Medium-High",
        _ => severity.ToString()
    };
}
