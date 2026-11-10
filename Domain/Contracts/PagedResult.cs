using System;
using System.Collections.Generic;

namespace pcb_deftek.Domain.Contracts;

/// <summary>
/// Generic envelope representing a bounded paginated collection of items.
/// </summary>
public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int PageNumber,
    int PageSize,
    int TotalItems)
{
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalItems / PageSize) : 0;
    public bool HasNextPage => PageNumber < TotalPages;
    public bool HasPreviousPage => PageNumber > 1;

    /// <summary>
    /// Factory creating an empty paginated result.
    /// </summary>
    public static PagedResult<T> Empty(int pageSize = 20) =>
        new(Array.Empty<T>(), 1, pageSize, 0);
}
