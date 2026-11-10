namespace pcb_deftek.Domain.Models;

/// <summary>
/// Specifies the 9 standardized PCB defect classification types detected by the YOLOv8 model.
/// Integer values correspond directly to model tensor output class indices.
/// </summary>
public enum DefectClass
{
    Short = 0,
    Spur = 1,
    SpuriousCopper = 2,
    Open = 3,
    MouseBite = 4,
    HoleBreakout = 5,
    ConductorScratch = 6,
    ConductorForeignObject = 7,
    BaseMaterialForeignObject = 8
}
