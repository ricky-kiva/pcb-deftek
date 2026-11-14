using System;
using System.Threading;
using System.Threading.Tasks;
using pcb_deftek.Domain.Contracts;

namespace pcb_deftek.Infrastructure.Export;

/// <summary>
/// Generates the audit PDF for the dashboard's active filter, and provides a preflight count so
/// the caller can decide between a no-match notification and a large-export confirmation.
/// </summary>
/// <remarks>
/// Implementations may delete inspection records whose images cannot be rendered, dispatch
/// Windows toasts on success/failure, and write temporary artifacts. They never expose QuestPDF
/// or presentation types. Cancellation is honored between phases; a cancelled export writes no
/// PDF and leaves no staging artifacts.
/// </remarks>
public interface IDashboardExportService
{
    /// <summary>
    /// Counts the inspections that an export for the specified filter would include.
    /// Uses the same date/class/severity predicate as the dashboard aggregates.
    /// </summary>
    /// <param name="filter">Dashboard filter snapshot.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of matching inspections; never negative.</returns>
    Task<int> CountMatchingInspectionsAsync(
        DashboardFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates and writes the audit PDF for the specified filter.
    /// </summary>
    /// <param name="filter">Dashboard filter snapshot.</param>
    /// <param name="progress">Optional intermediate progress sink.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An <see cref="DashboardExportResult"/> carrying the written path, or the no-match outcome
    /// when the filter selects no inspections.
    /// </returns>
    /// <exception cref="Common.Exceptions.ExportException">
    /// Thrown when the report cannot be generated or written.
    /// </exception>
    Task<DashboardExportResult> ExportAsync(
        DashboardFilter filter,
        IProgress<DashboardExportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
