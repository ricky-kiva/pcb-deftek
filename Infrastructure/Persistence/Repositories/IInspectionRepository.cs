using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Infrastructure.Persistence.Repositories;

/// <summary>
/// Contract for persisting, querying, and managing <see cref="InspectionRecord"/> aggregates.
/// </summary>
public interface IInspectionRepository
{
    /// <summary>
    /// Inserts a new inspection record and its associated defects in an atomic transaction.
    /// </summary>
    Task<Guid> InsertAsync(InspectionRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a single inspection aggregate by its unique identifier, including all associated defects.
    /// Returns null if not found.
    /// </summary>
    Task<InspectionRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a paginated list of inspection records matching the specified query filter.
    /// </summary>
    Task<PagedResult<InspectionRecord>> GetPagedAsync(InspectionQueryFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a paginated list of lightweight inspection summaries matching the specified query filter.
    /// Summaries carry the persisted defect count and do not load defect rows.
    /// </summary>
    /// <param name="filter">Filter, sorting, and pagination parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PagedResult<InspectionSummary>> GetSummariesPagedAsync(InspectionQueryFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves lightweight summaries for the specified inspection IDs in a single lookup.
    /// Unknown IDs are omitted from the result.
    /// </summary>
    /// <param name="ids">Inspection identifiers to retrieve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<InspectionSummary>> GetSummariesByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all inspection records created within a specific UTC date interval.
    /// </summary>
    Task<IReadOnlyList<InspectionRecord>> GetByDateRangeAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the user-assigned name of an existing inspection.
    /// Returns true if updated; false if the record was not found.
    /// </summary>
    Task<bool> UpdateNameAsync(Guid id, string newName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an inspection record by ID. Associated defects are automatically removed via cascading delete.
    /// Returns true if deleted; false if the record was not found.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes multiple inspection records in a single transaction. Associated defects are removed
    /// via cascading delete. Unknown IDs are ignored.
    /// </summary>
    /// <param name="ids">Inspection identifiers to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of records actually deleted.</returns>
    Task<int> DeleteManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total number of inspections recorded.
    /// </summary>
    Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default);
}
