using System.Threading;
using System.Threading.Tasks;
using pcb_deftek.Domain.Contracts;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Infrastructure.Persistence.Repositories;

/// <summary>
/// Read-only aggregate queries for the statistics dashboard.
/// </summary>
public interface IDashboardRepository
{
    /// <summary>
    /// Returns filtered dashboard totals, adaptive time buckets, and class distribution.
    /// </summary>
    /// <param name="filter">Date and defect filters. Empty class/severity sets mean no restriction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DashboardStatistics> GetStatisticsAsync(
        DashboardFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every inspection selected by the same predicate as <see cref="GetStatisticsAsync"/>,
    /// newest first, with only the defects that match the active class/severity filter loaded.
    /// </summary>
    /// <param name="filter">Date and defect filters. Empty class/severity sets mean no restriction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Inspection aggregates ordered by <c>CreatedAtUtc DESC, Id DESC</c>. PASS inspections appear
    /// only when no class/severity filter is active.
    /// </returns>
    Task<IReadOnlyList<InspectionRecord>> GetMatchingInspectionsAsync(
        DashboardFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the inspections selected by the same predicate as <see cref="GetStatisticsAsync"/>.
    /// Used as the export preflight without loading records.
    /// </summary>
    /// <param name="filter">Date and defect filters. Empty class/severity sets mean no restriction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of matching inspections; never negative.</returns>
    Task<int> GetMatchingInspectionCountAsync(
        DashboardFilter filter,
        CancellationToken cancellationToken = default);
}
