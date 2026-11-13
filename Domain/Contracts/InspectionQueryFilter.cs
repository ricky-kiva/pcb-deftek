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

    /// <summary>
    /// Gets the optional single-status filter retained for existing callers.
    /// Ignored when <see cref="Statuses"/> contains at least one value.
    /// </summary>
    public InspectionStatus? Status { get; init; }

    /// <summary>
    /// Gets the optional multi-select status filter. Empty or null does not add a multi-status
    /// clause and falls back to <see cref="Status"/>.
    /// When populated, it takes precedence over <see cref="Status"/>.
    /// </summary>
    public IReadOnlyCollection<InspectionStatus>? Statuses { get; init; }

    /// <summary>
    /// Gets the optional multi-select defect-class filter. A record matches when it contains
    /// at least one of the selected classes; empty or null applies no class filter.
    /// </summary>
    public IReadOnlyCollection<DefectClass>? DefectClasses { get; init; }

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
