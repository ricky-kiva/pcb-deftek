using System.Threading;
using System.Threading.Tasks;
using pcb_deftek.Domain.Contracts;

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
}
