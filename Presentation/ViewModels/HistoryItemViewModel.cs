using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Persistence.Repositories;
using pcb_deftek.Infrastructure.Services;
using pcb_deftek.Presentation.Models;

namespace pcb_deftek.Presentation.ViewModels;

/// <summary>
/// View model for a single history gallery card: display metadata, selection state,
/// lazy thumbnail hydration, and inline rename delegation.
/// </summary>
public sealed partial class HistoryItemViewModel : ObservableObject
{
    private readonly InspectionSummary _summary;
    private readonly InspectionNameEditor _nameEditor;
    private readonly ILogger<HistoryItemViewModel> _logger;

    /// <summary>
    /// Raised when a rename could not be persisted. The payload is a user-facing message.
    /// </summary>
    public event EventHandler<string>? RenameFailed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HistoryItemViewModel"/> class.
    /// </summary>
    public HistoryItemViewModel(
        InspectionSummary summary,
        IInspectionRepository repository,
        ILogger<HistoryItemViewModel>? logger = null)
    {
        _summary = summary ?? throw new ArgumentNullException(nameof(summary));
        _logger = logger ?? NullLogger<HistoryItemViewModel>.Instance;
        _nameEditor = new InspectionNameEditor(summary.Id, summary.Name, repository, null);

        _nameEditor.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(InspectionNameEditor.Name):
                    OnPropertyChanged(nameof(Name));
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
        };

        _nameEditor.RenameFailed += (_, message) => RenameFailed?.Invoke(this, message);
        CommitRenameCommand = new AsyncRelayCommand(() => CommitRenameAsync());
    }

    /// <summary>Gets the inspection identifier.</summary>
    public Guid Id => _summary.Id;

    /// <summary>Gets the persisted source image path.</summary>
    public string ImagePath => _summary.ImagePath;

    /// <summary>Gets the capture modality.</summary>
    public DetectionMode Mode => _summary.Mode;

    /// <summary>Gets the UTC creation timestamp.</summary>
    public DateTime CreatedAtUtc => _summary.CreatedAtUtc;

    /// <summary>Gets the local creation date used for grouping.</summary>
    public DateTime CreatedAtLocal => _summary.CreatedAtUtc.ToLocalTime();

    /// <summary>Gets the localized display date.</summary>
    public string DisplayDate => CreatedAtLocal.ToString("d", CultureInfo.CurrentCulture);

    /// <summary>Gets the localized display time.</summary>
    public string DisplayTime => CreatedAtLocal.ToString("t", CultureInfo.CurrentCulture);

    /// <summary>Gets the persisted defect count.</summary>
    public int DefectCount => _summary.DefectCount;

    /// <summary>Gets the PASS/FAIL badge text.</summary>
    public string StatusText => _summary.IsPass ? "PASS" : "FAIL";

    /// <summary>Gets a value indicating whether the inspection passed.</summary>
    public bool IsPass => _summary.IsPass;

    /// <summary>Gets the formatted defect counter.</summary>
    public string DefectCountText => DefectCount switch
    {
        0 => "0 Defects",
        1 => "1 Defect",
        _ => $"{DefectCount} Defects"
    };

    /// <summary>Gets the image provenance.</summary>
    public InspectionOrigin Origin => _summary.Origin;

    /// <summary>Gets the human-readable capture modality.</summary>
    public string ModeText => Origin == InspectionOrigin.Crop
        ? "Cropped"
        : Mode switch
        {
            DetectionMode.SingleFile => "Single File",
            DetectionMode.BatchFolder => "Batch Folder",
            DetectionMode.LiveCamera => "Live Camera",
            _ => Mode.ToString()
        };

    /// <summary>Gets the persisted inspection name.</summary>
    public string Name => _nameEditor.Name;

    /// <summary>
    /// Applies a name that was persisted elsewhere without writing to the repository again.
    /// </summary>
    public void ApplyPersistedName(string name) => _nameEditor.ApplyPersistedName(name);

    /// <summary>Gets or sets the in-progress name while editing.</summary>
    public string EditedName
    {
        get => _nameEditor.EditedName;
        set => _nameEditor.EditedName = value;
    }

    /// <summary>Gets a value indicating whether inline rename is active.</summary>
    public bool IsEditingName => _nameEditor.IsEditing;

    /// <summary>Gets a value indicating whether a rename is being persisted.</summary>
    public bool IsSavingName => _nameEditor.IsSaving;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumbnail))]
    private BitmapSource? _thumbnail;

    [ObservableProperty]
    private bool _isImageUnavailable;

    /// <summary>Gets a value indicating whether a thumbnail has been hydrated.</summary>
    public bool HasThumbnail => Thumbnail != null;

    /// <summary>
    /// Starts inline rename.
    /// </summary>
    [RelayCommand]
    public void BeginRename() => _nameEditor.BeginRename();

    /// <summary>
    /// Cancels inline rename and restores the persisted name.
    /// </summary>
    [RelayCommand]
    public void CancelRename() => _nameEditor.CancelRename();

    /// <summary>
    /// Persists the edited name.
    /// </summary>
    public Task<bool> CommitRenameAsync(CancellationToken cancellationToken = default)
        => _nameEditor.CommitRenameAsync(cancellationToken);

    /// <summary>Gets the command that commits the edited name.</summary>
    public IAsyncRelayCommand CommitRenameCommand { get; }

    /// <summary>
    /// Hydrates the card thumbnail from the source image.
    /// </summary>
    public async Task LoadThumbnailAsync(
        IHistoryThumbnailService thumbnailService,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(thumbnailService);

        try
        {
            var thumbnail = await thumbnailService
                .GetThumbnailAsync(ImagePath, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            Thumbnail = thumbnail;
            IsImageUnavailable = thumbnail is null;
        }
        catch (OperationCanceledException)
        {
            // Navigation moved on; leave the card in its loading state.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load history thumbnail for '{ImagePath}'.", ImagePath);
            IsImageUnavailable = true;
        }
    }
}
