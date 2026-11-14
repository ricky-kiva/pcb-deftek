using System.Collections.Generic;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Presentation.Models;

/// <summary>
/// Shared display labels for defect severity filter chips and knowledge cards.
/// </summary>
public static class DefectSeverityLabels
{
    private static readonly IReadOnlyDictionary<DefectSeverity, string> Labels =
        new Dictionary<DefectSeverity, string>
        {
            [DefectSeverity.Critical] = "Critical",
            [DefectSeverity.High] = "High",
            [DefectSeverity.MediumHigh] = "Medium-high",
            [DefectSeverity.Medium] = "Medium",
            [DefectSeverity.LowMedium] = "Low-medium"
        };

    /// <summary>Gets the display label for a severity.</summary>
    public static string Get(DefectSeverity severity) =>
        Labels.TryGetValue(severity, out string? label) ? label : severity.ToString();
}
