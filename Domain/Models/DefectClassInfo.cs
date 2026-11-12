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
    string Action);
