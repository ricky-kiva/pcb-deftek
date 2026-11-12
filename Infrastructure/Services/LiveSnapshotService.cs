using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Inference;
using pcb_deftek.Infrastructure.Persistence.Repositories;

namespace pcb_deftek.Infrastructure.Services;

/// <summary>
/// Default implementation of <see cref="ILiveSnapshotService"/> that persists live camera snapshots
/// (both raw and annotated overlay images) to local storage and saves <see cref="InspectionRecord"/>
/// aggregates to SQLite with <see cref="DetectionMode.LiveCamera"/>.
/// </summary>
public class LiveSnapshotService : ILiveSnapshotService
{
    private readonly IInspectionRepository _repository;
    private readonly ILiveInspectionEngine _liveInspectionEngine;
    private readonly ILogger<LiveSnapshotService> _logger;
    private int _snapshotSequence;

    /// <inheritdoc />
    public string CapturesDirectoryPath { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveSnapshotService"/> class.
    /// </summary>
    /// <param name="repository">The inspection repository for SQLite persistence.</param>
    /// <param name="liveInspectionEngine">The live inspection engine providing active frames.</param>
    /// <param name="capturesDirectoryPath">Optional custom directory path for saved images.</param>
    /// <param name="logger">Optional logger instance.</param>
    public LiveSnapshotService(
        IInspectionRepository repository,
        ILiveInspectionEngine liveInspectionEngine,
        string? capturesDirectoryPath = null,
        ILogger<LiveSnapshotService>? logger = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _liveInspectionEngine = liveInspectionEngine ?? throw new ArgumentNullException(nameof(liveInspectionEngine));
        _logger = logger ?? NullLogger<LiveSnapshotService>.Instance;

        CapturesDirectoryPath = !string.IsNullOrWhiteSpace(capturesDirectoryPath)
            ? Path.GetFullPath(capturesDirectoryPath)
            : GetDefaultCapturesDirectoryPath();
    }

    /// <summary>
    /// Gets the default local application data directory for live camera captures.
    /// </summary>
    public static string GetDefaultCapturesDirectoryPath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "PcbDeftek", "pcb-deftek", "Captures");
    }

    /// <inheritdoc />
    public Task<InspectionRecord> CaptureLatestAsync(
        string? customName = null,
        CancellationToken cancellationToken = default)
    {
        var latestFrame = _liveInspectionEngine.LatestResult;
        if (latestFrame == null)
        {
            throw new CameraCaptureException("No active live camera frame is available for snapshot capture.");
        }

        return CaptureSnapshotAsync(latestFrame, customName, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<InspectionRecord> CaptureSnapshotAsync(
        LiveFrameResult frameResult,
        string? customName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frameResult);
        cancellationToken.ThrowIfCancellationRequested();

        using var rawClone = frameResult.TryCloneRawFrame();
        if (rawClone == null)
        {
            throw new CameraCaptureException("Active live camera frame has been disposed and cannot be captured.");
        }

        var inspectionId = Guid.NewGuid();
        var createdAtUtc = DateTime.UtcNow;
        int seq = Interlocked.Increment(ref _snapshotSequence);

        string boardName = !string.IsNullOrWhiteSpace(customName)
            ? customName.Trim()
            : $"{createdAtUtc:yyyy-MM-dd}-{seq:D3}";

        Directory.CreateDirectory(CapturesDirectoryPath);

        string rawImagePath = Path.Combine(CapturesDirectoryPath, $"{inspectionId:N}_raw.png");
        string annotatedImagePath = GetAnnotatedImagePath(rawImagePath);
        var annotatedBitmap = frameResult.AnnotatedImage;

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 1. Save unannotated raw camera frame
            if (!Cv2.ImWrite(rawImagePath, rawClone))
            {
                throw new CameraCaptureException($"Failed to encode raw camera frame to '{rawImagePath}'.");
            }

            // 2. Save annotated overlay image
            using var fileStream = new FileStream(annotatedImagePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(annotatedBitmap));
            encoder.Save(fileStream);
        }, cancellationToken).ConfigureAwait(false);

        var mappedDefects = frameResult.Inference.Defects
            .Select(d => new DetectedDefect(
                Guid.NewGuid(),
                inspectionId,
                d.ClassInfo.Code,
                d.Confidence,
                d.BoundingBox))
            .ToList();

        var record = InspectionRecord.Create(
            name: boardName,
            mode: DetectionMode.LiveCamera,
            imagePath: rawImagePath,
            defects: mappedDefects,
            id: inspectionId,
            createdAtUtc: createdAtUtc);

        await _repository.InsertAsync(record, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Captured live camera snapshot '{BoardName}' ({InspectionId}): {Status} with {DefectCount} defect(s).",
            record.Name,
            record.Id,
            record.Status,
            record.DefectCount);

        return record;
    }

    /// <inheritdoc />
    public string GetAnnotatedImagePath(string rawImagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawImagePath);

        if (rawImagePath.EndsWith("_raw.png", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(rawImagePath.AsSpan(0, rawImagePath.Length - "_raw.png".Length), "_annotated.png");
        }

        string dir = Path.GetDirectoryName(rawImagePath) ?? CapturesDirectoryPath;
        string nameWithoutExt = Path.GetFileNameWithoutExtension(rawImagePath);
        return Path.Combine(dir, $"{nameWithoutExt}_annotated.png");
    }
}
