namespace pcb_deftek.Domain.Models;

/// <summary>
/// Specifies the diagnostic severity risk grading for a detected PCB defect.
/// </summary>
public enum DefectSeverity
{
    LowMedium = 0,
    Medium = 1,
    MediumHigh = 2,
    High = 3,
    Critical = 4
}
