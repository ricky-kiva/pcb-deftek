using System;
using System.Collections.Generic;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Domain.Contracts;

/// <summary>
/// Input boundary command submitted by capture/inference workflows to record a new inspection.
/// </summary>
public record CreateInspectionCommand(
    string Name,
    DateTime CreatedAtUtc,
    DetectionMode Mode,
    string ImagePath,
    IReadOnlyList<CreateDefectInput> Defects);
