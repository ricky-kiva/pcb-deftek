namespace pcb_deftek.Domain.Models;

/// <summary>
/// Immutable value object containing standardized diagnostic definitions, root cause analysis,
/// severity level, and remediation recommendations for a specific defect class.
/// </summary>
public record DefectClassInfo(
    DefectClass Class,
    int ClassId,
    string Code,
    string Abbreviation,
    string DisplayName,
    string Explanation,
    DefectSeverity Severity,
    string RootCause,
    string Impact,
    string Action)
{
    /// <summary>
    /// Gets the single-source display color for this defect class as an opaque <c>#RRGGBB</c> value.
    /// OpenCV annotation and WPF overlays both derive their colors from this value.
    /// </summary>
    public string ColorHex => Class switch
    {
        DefectClass.Short => "#0066FF",
        DefectClass.Spur => "#00DCDC",
        DefectClass.SpuriousCopper => "#FFFFFF",
        DefectClass.Open => "#4FA050",
        DefectClass.MouseBite => "#00308C",
        DefectClass.HoleBreakout => "#FF69B4",
        DefectClass.ConductorScratch => "#E81123",
        DefectClass.ConductorForeignObject => "#FFD700",
        DefectClass.BaseMaterialForeignObject => "#2ECC71",
        _ => "#C42B1C"
    };
}