using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Infrastructure.Persistence.Repositories;

/// <summary>
/// Contract for managing and querying localized <see cref="DetectedDefect"/> entities.
/// </summary>
public interface IDefectRepository
{
    /// <summary>
    /// Inserts a batch of detected defects belonging to a specified inspection inside a transaction.
    /// </summary>
    Task InsertBatchAsync(Guid inspectionId, IReadOnlyList<DetectedDefect> defects, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all detected defects associated with a specific inspection.
    /// </summary>
    Task<IReadOnlyList<DetectedDefect>> GetByInspectionIdAsync(Guid inspectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the defect count distribution grouped by class code across all inspections within an optional date range.
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> GetClassDistributionAsync(DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the aggregate total defect count across all inspections within an optional date range.
    /// </summary>
    Task<int> GetTotalDefectCountAsync(DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken cancellationToken = default);
}
