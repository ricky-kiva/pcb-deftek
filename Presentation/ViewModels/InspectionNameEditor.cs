using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pcb_deftek.Infrastructure.Persistence.Repositories;

namespace pcb_deftek.Presentation.ViewModels;

/// <summary>
/// Reusable inline-rename state shared by history cards and the inspection viewer.
/// Owns validation, persistence, and revert-on-failure but no UI types.
/// </summary>
public sealed partial class InspectionNameEditor : ObservableObject
{
    private readonly Guid _inspectionId;
    private readonly IInspectionRepository _repository;
    private readonly ILogger<InspectionNameEditor> _logger;
    private string _name;

    /// <summary>
    /// Raised when a rename could not be persisted. The payload is a user-facing message.
    /// </summary>
    public event EventHandler<string>? RenameFailed;

    /// <summary>
    /// Initializes a new instance of the <see cref="InspectionNameEditor"/> class.
    /// </summary>
    public InspectionNameEditor(
        Guid inspectionId,
        string initialName,
        IInspectionRepository repository,
        ILogger<InspectionNameEditor>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(initialName);
        _inspectionId = inspectionId;
        _name = initialName.Trim();
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? NullLogger<InspectionNameEditor>.Instance;
        _editedName = _name;
    }

    /// <summary>
    /// Gets the persisted inspection name.
    /// </summary>
    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    [ObservableProperty]
    private string _editedName;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isSaving;

    /// <summary>
    /// Starts inline editing and seeds the edited value with the persisted name.
    /// </summary>
    /// <returns>True when edit mode was entered; false when already editing.</returns>
    public bool BeginRename()
    {
        if (IsEditing)
        {
            return false;
        }

        EditedName = Name;
        IsEditing = true;
        return true;
    }

    /// <summary>
    /// Cancels editing and restores the persisted name.
    /// </summary>
    public void CancelRename()
    {
        EditedName = Name;
        IsEditing = false;
    }

    /// <summary>
    /// Validates and persists the edited name. Empty input and unchanged names are not written.
    /// On failure the persisted name is restored and <see cref="RenameFailed"/> is raised.
    /// </summary>
    /// <returns>True when the name is persisted or unchanged; false when rejected or failed.</returns>
    public async Task<bool> CommitRenameAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEditing || IsSaving)
        {
            return false;
        }

        string trimmed = (EditedName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            CancelRename();
            return false;
        }

        if (string.Equals(trimmed, Name, StringComparison.Ordinal))
        {
            IsEditing = false;
            EditedName = Name;
            return true;
        }

        IsSaving = true;
        try
        {
            bool updated = await _repository
                .UpdateNameAsync(_inspectionId, trimmed, cancellationToken);

            if (updated)
            {
                Name = trimmed;
                EditedName = trimmed;
                IsEditing = false;
                return true;
            }

            CancelRename();
            RenameFailed?.Invoke(this, $"Inspection '{Name}' no longer exists.");
            return false;
        }
        catch (OperationCanceledException)
        {
            CancelRename();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to rename inspection {InspectionId}.", _inspectionId);
            CancelRename();
            RenameFailed?.Invoke(this, $"Could not rename '{Name}': {ex.Message}");
            return false;
        }
        finally
        {
            IsSaving = false;
        }
    }
}
