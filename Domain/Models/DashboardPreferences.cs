using System;
using System.Collections.Generic;
using System.Linq;

namespace pcb_deftek.Domain.Models;

/// <summary>
/// Column count for the dashboard metric-card grid.
/// </summary>
public enum DashboardColumnLayout
{
    OneColumn = 1,
    TwoColumns = 2
}

/// <summary>
/// Canonical keys and normalization for dashboard metric-card order.
/// </summary>
public static class DashboardCardOrderPolicy
{
    public const string TotalPcbs = "total-pcbs";
    public const string TotalDefects = "total-defects";
    public const string PassFail = "pass-fail";
    public const string Distribution = "distribution";

    private static readonly string[] DefaultKeys =
    {
        TotalPcbs,
        TotalDefects,
        PassFail,
        Distribution
    };

    /// <summary>Gets the standard four-card order.</summary>
    public static IReadOnlyList<string> DefaultOrder { get; } = Array.AsReadOnly(DefaultKeys);

    /// <summary>
    /// Normalizes a stored order: keeps recognized keys, removes duplicates, and appends
    /// any missing standard cards. Missing/invalid input yields the default order.
    /// </summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string>? order)
    {
        var normalized = new List<string>(DefaultKeys.Length);
        if (order is not null)
        {
            foreach (string key in order)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                string? canonical = DefaultKeys.FirstOrDefault(
                    candidate => string.Equals(candidate, key.Trim(), StringComparison.OrdinalIgnoreCase));
                if (canonical is not null && !normalized.Contains(canonical, StringComparer.Ordinal))
                {
                    normalized.Add(canonical);
                }
            }
        }

        foreach (string key in DefaultKeys)
        {
            if (!normalized.Contains(key, StringComparer.Ordinal))
            {
                normalized.Add(key);
            }
        }

        return normalized;
    }

    /// <summary>Parses a comma-separated persisted order.</summary>
    public static IReadOnlyList<string> Parse(string? persisted) =>
        Normalize(persisted?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>Serializes an order to its persisted form.</summary>
    public static string Serialize(IEnumerable<string>? order) =>
        string.Join(',', Normalize(order));
}

/// <summary>
/// Parsing and fallback policy for the persisted dashboard column layout.
/// </summary>
public static class DashboardColumnLayoutPolicy
{
    public static DashboardColumnLayout Parse(string? persisted) =>
        Normalize(Enum.TryParse(persisted, ignoreCase: true, out DashboardColumnLayout layout)
            ? layout
            : default);

    public static DashboardColumnLayout Normalize(DashboardColumnLayout layout) =>
        Enum.IsDefined(layout) ? layout : DashboardColumnLayout.TwoColumns;
}
