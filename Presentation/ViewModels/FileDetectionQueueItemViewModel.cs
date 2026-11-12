using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Inference;

namespace pcb_deftek.Presentation.ViewModels;

/// <summary>
/// View model backing an individual image file item in the batch detection queue or results grid.
/// </summary>
public partial class FileDetectionQueueItemViewModel : ObservableObject
{
    /// <summary>
    /// Gets the absolute file path on disk.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Gets the filename including extension.
    /// </summary>
    public string FileName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending))]
    [NotifyPropertyChangedFor(nameof(IsProcessing))]
    [NotifyPropertyChangedFor(nameof(IsPass))]
    [NotifyPropertyChangedFor(nameof(IsFail))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeText))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeTooltip))]
    [NotifyPropertyChangedFor(nameof(DefectSummary))]
    private BatchItemStatus _status = BatchItemStatus.Pending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefectSummary))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeTooltip))]
    private int _defectCount;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedDuration))]
    private TimeSpan _duration = TimeSpan.Zero;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private Guid? _inspectionId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefectSummary))]
    private InferenceResult? _result;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileDetectionQueueItemViewModel"/> class.
    /// </summary>
    /// <param name="filePath">Absolute path of the PCB image file.</param>
    public FileDetectionQueueItemViewModel(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
    }

    /// <summary>Gets a value indicating whether the item is pending processing.</summary>
    public bool IsPending => Status == BatchItemStatus.Pending;

    /// <summary>Gets a value indicating whether the item is currently being processed.</summary>
    public bool IsProcessing => Status == BatchItemStatus.Processing;

    /// <summary>Gets a value indicating whether the item passed inspection with zero defects.</summary>
    public bool IsPass => Status == BatchItemStatus.Pass;

    /// <summary>Gets a value indicating whether the item failed inspection with one or more defects.</summary>
    public bool IsFail => Status == BatchItemStatus.Fail;

    /// <summary>Gets a value indicating whether an error occurred during inspection.</summary>
    public bool IsError => Status == BatchItemStatus.Error;

    /// <summary>
    /// Gets a user-facing label for the status badge.
    /// </summary>
    public string StatusBadgeText => Status switch
    {
        BatchItemStatus.Pending => "Pending",
        BatchItemStatus.Processing => "Processing",
        BatchItemStatus.Pass => "PASS",
        BatchItemStatus.Fail => "FAIL",
        BatchItemStatus.Error => "Error",
        _ => Status.ToString()
    };

    /// <summary>
    /// Gets a human-readable formatted duration string (e.g. "45 ms" or "-").
    /// </summary>
    public string FormattedDuration => Duration > TimeSpan.Zero
        ? $"{Duration.TotalMilliseconds:N0} ms"
        : "-";

    /// <summary>
    /// Gets a detailed tooltip explaining the inspection status for the operator.
    /// </summary>
    public string StatusBadgeTooltip => Status switch
    {
        BatchItemStatus.Pass => "Inspection Passed: Clean board with zero defects detected.",
        BatchItemStatus.Fail => $"Inspection Failed: {DefectCount} defect(s) detected ({DefectSummary}).",
        BatchItemStatus.Error => $"Inspection Error: {ErrorMessage ?? "Unexpected processing failure."}",
        BatchItemStatus.Processing => "Currently analyzing PCB board image...",
        _ => "Queued for automated optical inspection."
    };

    /// <summary>
    /// Gets a human-readable summary of detected defects or status.
    /// </summary>
    public string DefectSummary
    {
        get
        {
            if (Status == BatchItemStatus.Pending) return "Queued";
            if (Status == BatchItemStatus.Processing) return "Inspecting...";
            if (Status == BatchItemStatus.Error) return ErrorMessage ?? "Error";
            if (Status == BatchItemStatus.Pass) return "Clean (No defects)";

            if (Result?.Defects != null && Result.Defects.Count > 0)
            {
                return string.Join(", ", Result.Defects.Select(d => $"{d.ClassName} ({d.Confidence:P0})"));
            }

            return DefectCount > 0 ? $"{DefectCount} defect(s)" : "Clean";
        }
    }

    /// <summary>
    /// Updates this ViewModel's properties from a completed <see cref="BatchInspectionItem"/>.
    /// </summary>
    /// <param name="item">The completed batch item data.</param>
    public void UpdateFrom(BatchInspectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Status = item.Status;
        DefectCount = item.DefectCount;
        Duration = item.Duration;
        ErrorMessage = item.ErrorMessage;
        InspectionId = item.InspectionId;
        Result = item.InferenceResult;
    }
}
