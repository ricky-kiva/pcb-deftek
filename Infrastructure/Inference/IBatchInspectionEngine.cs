using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Infrastructure.Inference;

/// <summary>
/// Service contract orchestrating high-throughput, asynchronous batch defect inspection across
/// collections of PCB images with rolling ETA estimation and database persistence.
/// </summary>
public interface IBatchInspectionEngine
{
    /// <summary>
    /// Executes sequential PCB defect detection across a collection of image file paths off the UI thread.
    /// Emits real-time progress reports and persists results to SQLite.
    /// </summary>
    /// <param name="filePaths">The collection of image file paths to inspect.</param>
    /// <param name="progress">Optional progress reporter receiving <see cref="BatchProgressReport"/>.</param>
    /// <param name="confidenceThreshold">Optional confidence threshold override.</param>
    /// <param name="cancellationToken">Cancellation token to abort remaining batch processing.</param>
    /// <returns>A summary of the batch inspection session.</returns>
    Task<BatchInspectionSummary> ExecuteBatchAsync(
        IReadOnlyList<string> filePaths,
        IProgress<BatchProgressReport>? progress = null,
        float? confidenceThreshold = null,
        CancellationToken cancellationToken = default);
}
