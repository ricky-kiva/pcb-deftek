using System;
using System.IO;

namespace pcb_deftek.Infrastructure.Export;

/// <summary>Outcome of an export request.</summary>
public enum DashboardExportOutcome
{
    Exported = 0,
    NoMatches = 1
}

/// <summary>
/// Result of a dashboard export: either a written PDF or a no-match outcome that produced no file.
/// </summary>
public sealed record DashboardExportResult
{
    private DashboardExportResult(
        DashboardExportOutcome outcome,
        string? filePath,
        int matchingInspections)
    {
        Outcome = outcome;
        FilePath = filePath;
        MatchingInspections = matchingInspections;
    }

    /// <summary>Gets the export outcome.</summary>
    public DashboardExportOutcome Outcome { get; }

    /// <summary>Gets the absolute path of the written PDF; <see langword="null"/> when no file was written.</summary>
    public string? FilePath { get; }

    /// <summary>Gets the number of inspections considered by the export.</summary>
    public int MatchingInspections { get; }

    /// <summary>Gets a value indicating whether a PDF was written.</summary>
    public bool HasFile => Outcome == DashboardExportOutcome.Exported;

    /// <summary>Creates a successful result for a written PDF.</summary>
    /// <param name="filePath">Absolute or relative path of the written PDF.</param>
    /// <param name="matchingInspections">Number of inspections included in the report; at least one.</param>
    /// <returns>The successful result.</returns>
    public static DashboardExportResult Exported(string filePath, int matchingInspections)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (matchingInspections < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(matchingInspections),
                matchingInspections,
                "An exported report requires at least one inspection.");
        }

        return new DashboardExportResult(
            DashboardExportOutcome.Exported,
            Path.GetFullPath(filePath.Trim()),
            matchingInspections);
    }

    /// <summary>Creates a result for a request that matched no inspections and wrote no file.</summary>
    /// <returns>The no-match result.</returns>
    public static DashboardExportResult NoMatches() =>
        new(DashboardExportOutcome.NoMatches, null, 0);
}
