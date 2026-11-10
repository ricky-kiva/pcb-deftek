using System;
using System.Collections.Generic;

namespace pcb_deftek.Domain.Contracts;

/// <summary>
/// Represents a paginated collection of items with boundary navigation metadata.
/// </summary>
/// <typeparam name="T">The type of items in the page.</typeparam>
public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int PageNumber,
    int PageSize,
    int TotalItems)
{
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalItems / PageSize) : 0;
    public bool HasNextPage => PageNumber < TotalPages;
    public bool HasPreviousPage => PageNumber > 1;
}
