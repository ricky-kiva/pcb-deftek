namespace pcb_deftek.Domain.Models;

/// <summary>
/// Specifies the overall PASS/FAIL evaluation status for a PCB inspection.
/// Pass = 0 defects, Fail = 1 or more defects.
/// </summary>
public enum InspectionStatus
{
    Pass = 0,
    Fail = 1
}
