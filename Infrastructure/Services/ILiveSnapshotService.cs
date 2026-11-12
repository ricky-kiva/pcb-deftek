using System.Threading;
using System.Threading.Tasks;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Infrastructure.Services;

/// <summary>
/// Service contract for capturing live camera inspection frames, persisting raw and annotated images
/// to application storage, and recording <see cref="InspectionRecord"/> entities in SQLite with <see cref="DetectionMode.LiveCamera"/>.
/// </summary>
public interface ILiveSnapshotService
{
    /// <summary>
    /// Gets the local directory path where captured live camera images are stored.
    /// </summary>
    string CapturesDirectoryPath { get; }

    /// <summary>
    /// Captures the specified <see cref="LiveFrameResult"/>, saves both raw and annotated images to disk,
    /// and persists the inspection record and its bounding boxes into SQLite.
    /// </summary>
    /// <param name="frameResult">The processed live frame result to capture.</param>
    /// <param name="customName">Optional custom board name; if null, generates a default date-numbered name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted <see cref="InspectionRecord"/>.</returns>
    Task<InspectionRecord> CaptureSnapshotAsync(
        LiveFrameResult frameResult,
        string? customName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures the most recent frame from the active live inspection engine, saves images to disk,
    /// and persists the inspection record into SQLite.
    /// </summary>
    /// <param name="customName">Optional custom board name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted <see cref="InspectionRecord"/>.</returns>
    Task<InspectionRecord> CaptureLatestAsync(
        string? customName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the companion annotated overlay image path corresponding to a captured raw image path.
    /// </summary>
    /// <param name="rawImagePath">The raw image file path stored on the inspection record.</param>
    /// <returns>The full file path of the annotated overlay image.</returns>
    string GetAnnotatedImagePath(string rawImagePath);
}
