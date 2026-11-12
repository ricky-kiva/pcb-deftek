using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Persistence.Repositories;

namespace pcb_deftek.Infrastructure.Inference;

/// <summary>
/// Orchestrates asynchronous, sequential PCB defect detection across collections of image files,
/// computing rolling average ETAs and persisting inspection records to SQLite.
/// </summary>
public class BatchInspectionEngine : IBatchInspectionEngine
{
    private const int MaxRollingWindowSize = 10;
    private readonly IPcbDefectDetector _detector;
    private readonly IInspectionRepository _repository;
    private readonly ILogger<BatchInspectionEngine> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BatchInspectionEngine"/> class.
    /// </summary>
    public BatchInspectionEngine(
        IPcbDefectDetector detector,
        IInspectionRepository repository,
        ILogger<BatchInspectionEngine>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(repository);

        _detector = detector;
        _repository = repository;
        _logger = logger ?? NullLogger<BatchInspectionEngine>.Instance;
    }

    /// <inheritdoc />
    public async Task<BatchInspectionSummary> ExecuteBatchAsync(
        IReadOnlyList<string> filePaths,
        IProgress<BatchProgressReport>? progress = null,
        float? confidenceThreshold = null,
        CancellationToken cancellationToken = default)
    {
        if (filePaths == null || filePaths.Count == 0)
        {
            return new BatchInspectionSummary(
                Status: BatchStatus.Completed,
                TotalCount: 0,
                ProcessedCount: 0,
                PassCount: 0,
                FailCount: 0,
                ErrorCount: 0,
                TotalDuration: TimeSpan.Zero,
                Items: Array.Empty<BatchInspectionItem>());
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new BatchInspectionSummary(
                Status: BatchStatus.Cancelled,
                TotalCount: filePaths.Count,
                ProcessedCount: 0,
                PassCount: 0,
                FailCount: 0,
                ErrorCount: 0,
                TotalDuration: TimeSpan.Zero,
                Items: Array.Empty<BatchInspectionItem>());
        }

        return await Task.Run(async () =>
        {
            var items = new List<BatchInspectionItem>(filePaths.Count);
            var totalStopwatch = Stopwatch.StartNew();
            var rollingDurations = new Queue<double>(MaxRollingWindowSize);
            var mode = filePaths.Count == 1 ? DetectionMode.SingleFile : DetectionMode.BatchFolder;

            int passCount = 0;
            int failCount = 0;
            int errorCount = 0;
            int processedCount = 0;
            var status = BatchStatus.Running;

            for (int i = 0; i < filePaths.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    status = BatchStatus.Cancelled;
                    break;
                }

                var path = filePaths[i];
                var fileName = Path.GetFileName(path);
                var boardName = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(boardName))
                {
                    boardName = $"PCB-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{i + 1}";
                }

                // Initial item notification
                progress?.Report(new BatchProgressReport(
                    TotalCount: filePaths.Count,
                    ProcessedCount: processedCount,
                    PassCount: passCount,
                    FailCount: failCount,
                    ErrorCount: errorCount,
                    ElapsedTime: totalStopwatch.Elapsed,
                    EstimatedTimeRemaining: CalculateEta(filePaths.Count, processedCount, rollingDurations),
                    CurrentFileName: fileName,
                    LatestCompletedItem: items.Count > 0 ? items[^1] : null));

                var itemStopwatch = Stopwatch.StartNew();
                BatchInspectionItem item;

                try
                {
                    var result = await _detector.DetectFromFileAsync(
                        path,
                        confidenceThreshold: confidenceThreshold,
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    itemStopwatch.Stop();
                    var duration = itemStopwatch.Elapsed;

                    AddRollingDuration(rollingDurations, duration.TotalSeconds);

                    var record = InspectionRecord.Create(
                        name: boardName,
                        mode: mode,
                        imagePath: path,
                        defects: result.Defects);

                    var inspectionId = await _repository.InsertAsync(record, cancellationToken).ConfigureAwait(false);

                    if (result.Passed)
                    {
                        passCount++;
                    }
                    else
                    {
                        failCount++;
                    }

                    item = new BatchInspectionItem(
                        FilePath: path,
                        FileName: fileName,
                        Status: result.Passed ? BatchItemStatus.Pass : BatchItemStatus.Fail,
                        DefectCount: result.DefectCount,
                        Duration: duration,
                        ErrorMessage: null,
                        InspectionId: inspectionId,
                        InferenceResult: result);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    status = BatchStatus.Cancelled;
                    break;
                }
                catch (Exception ex)
                {
                    itemStopwatch.Stop();
                    var duration = itemStopwatch.Elapsed;
                    AddRollingDuration(rollingDurations, duration.TotalSeconds);

                    _logger.LogWarning(ex, "Failed to inspect file '{FilePath}' during batch detection.", path);

                    errorCount++;
                    item = new BatchInspectionItem(
                        FilePath: path,
                        FileName: fileName,
                        Status: BatchItemStatus.Error,
                        DefectCount: 0,
                        Duration: duration,
                        ErrorMessage: ex.Message);
                }

                items.Add(item);
                processedCount++;

                progress?.Report(new BatchProgressReport(
                    TotalCount: filePaths.Count,
                    ProcessedCount: processedCount,
                    PassCount: passCount,
                    FailCount: failCount,
                    ErrorCount: errorCount,
                    ElapsedTime: totalStopwatch.Elapsed,
                    EstimatedTimeRemaining: CalculateEta(filePaths.Count, processedCount, rollingDurations),
                    CurrentFileName: fileName,
                    LatestCompletedItem: item));
            }

            totalStopwatch.Stop();

            if (status != BatchStatus.Cancelled)
            {
                status = BatchStatus.Completed;
            }

            return new BatchInspectionSummary(
                Status: status,
                TotalCount: filePaths.Count,
                ProcessedCount: processedCount,
                PassCount: passCount,
                FailCount: failCount,
                ErrorCount: errorCount,
                TotalDuration: totalStopwatch.Elapsed,
                Items: items);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void AddRollingDuration(Queue<double> queue, double seconds)
    {
        if (queue.Count >= MaxRollingWindowSize)
        {
            queue.Dequeue();
        }

        queue.Enqueue(seconds);
    }

    private static TimeSpan? CalculateEta(int totalCount, int processedCount, Queue<double> rollingDurations)
    {
        if (processedCount >= totalCount)
        {
            return TimeSpan.Zero;
        }

        // Show "Calculating..." (null) until at least 2 items complete to avoid initial spikes
        if (processedCount < 2 || rollingDurations.Count == 0)
        {
            return null;
        }

        double averageSeconds = rollingDurations.Average();
        int remaining = totalCount - processedCount;
        double etaSeconds = Math.Max(0.0, remaining * averageSeconds);

        return TimeSpan.FromSeconds(etaSeconds);
    }
}
