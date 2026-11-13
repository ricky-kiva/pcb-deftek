using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace pcb_deftek.Infrastructure.Services;

/// <summary>
/// Outcome of deleting one or more inspection records and their app-owned capture files.
/// </summary>
/// <param name="DeletedRecordCount">Number of database records removed.</param>
/// <param name="DeletedFileCount">Number of app-owned capture files removed from disk.</param>
/// <param name="FailedFilePaths">Capture files that could not be removed after the database commit.</param>
public sealed record InspectionDeletionResult(
    int DeletedRecordCount,
    int DeletedFileCount,
    IReadOnlyList<string> FailedFilePaths)
{
    /// <summary>
    /// Gets a value indicating whether one or more capture files could not be deleted.
    /// </summary>
    public bool HasFileFailures => FailedFilePaths.Count > 0;
}

/// <summary>
/// Deletes inspection records and cleans up only the capture files the application owns.
/// User-owned source images are never modified or deleted.
/// </summary>
public interface IInspectionDeletionService
{
    /// <summary>
    /// Deletes a single inspection record and its app-owned capture files.
    /// </summary>
    /// <param name="id">Inspection identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deletion outcome.</returns>
    Task<InspectionDeletionResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes multiple inspection records in one database transaction, then cleans up their
    /// app-owned capture files. Unknown identifiers are ignored.
    /// </summary>
    /// <param name="ids">Inspection identifiers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deletion outcome.</returns>
    Task<InspectionDeletionResult> DeleteManyAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);
}
