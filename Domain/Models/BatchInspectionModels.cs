using System;
using System.Collections.Generic;
using pcb_deftek.Infrastructure.Inference;

namespace pcb_deftek.Domain.Models;

/// <summary>
/// Defines the overall lifecycle state of a batch defect inspection job.
/// </summary>
public enum BatchStatus
{
    /// <summary>Job is queued or awaiting execution.</summary>
    Pending = 0,

    /// <summary>Job is actively processing items.</summary>
    Running = 1,

    /// <summary>All items completed processing.</summary>
    Completed = 2,

    /// <summary>Job was cancelled before all items were processed.</summary>
    Cancelled = 3,

    /// <summary>Job failed due to an unrecoverable system exception.</summary>
    Faulted = 4
}

/// <summary>
/// Defines the inspection status of an individual image item within a batch.
/// </summary>
public enum BatchItemStatus
{
    /// <summary>Item is queued and waiting for inference.</summary>
    Pending = 0,

    /// <summary>Item is currently undergoing image decoding, inference, or persistence.</summary>
    Processing = 1,

    /// <summary>Inspection completed with zero detected defects.</summary>
    Pass = 2,

    /// <summary>Inspection completed with one or more detected defects.</summary>
    Fail = 3,

    /// <summary>Inspection encountered an error (e.g. corrupted file or I/O failure).</summary>
    Error = 4
}

/// <summary>
/// Encapsulates the execution state and results for an individual file in a batch.
/// </summary>
public record BatchInspectionItem(
    string FilePath,
    string FileName,
    BatchItemStatus Status,
    int DefectCount,
    TimeSpan Duration,
    string? ErrorMessage = null,
    Guid? InspectionId = null,
    InferenceResult? InferenceResult = null);

/// <summary>
/// Real-time progress payload emitted by the batch inspection engine.
/// </summary>
public record BatchProgressReport(
    int TotalCount,
    int ProcessedCount,
    int PassCount,
    int FailCount,
    int ErrorCount,
    TimeSpan ElapsedTime,
    TimeSpan? EstimatedTimeRemaining,
    string? CurrentFileName = null,
    BatchInspectionItem? LatestCompletedItem = null)
{
    /// <summary>
    /// Gets the completion percentage from 0 to 100.
    /// </summary>
    public int PercentComplete => TotalCount > 0
        ? (int)Math.Clamp(Math.Round((double)ProcessedCount / TotalCount * 100.0), 0, 100)
        : 0;

    /// <summary>
    /// Gets the processing throughput speed in items per second.
    /// </summary>
    public double SpeedItemsPerSecond => ElapsedTime.TotalSeconds > 0
        ? ProcessedCount / ElapsedTime.TotalSeconds
        : 0.0;
}

/// <summary>
/// Encapsulates the aggregated final results of a completed or cancelled batch inspection session.
/// </summary>
public record BatchInspectionSummary(
    BatchStatus Status,
    int TotalCount,
    int ProcessedCount,
    int PassCount,
    int FailCount,
    int ErrorCount,
    TimeSpan TotalDuration,
    IReadOnlyList<BatchInspectionItem> Items);
