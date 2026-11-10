using System;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Domain.Contracts;

/// <summary>
/// Query filter and pagination parameters for historical inspection retrieval.
/// </summary>
public class InspectionQueryFilter
{
    private int _pageSize = 20;

    public DateTime? StartDateUtc { get; init; }
    public DateTime? EndDateUtc { get; init; }
    public InspectionStatus? Status { get; init; }
    public DetectionMode? Mode { get; init; }
    public int PageNumber { get; init; } = 1;

    /// <summary>
    /// Gets or sets the page size, bounded to a maximum of 100 items per page.
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = Math.Clamp(value, 1, 100);
    }

    public string? SortBy { get; init; }
    public bool SortDescending { get; init; } = true;
}
