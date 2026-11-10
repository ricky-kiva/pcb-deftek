using System;

namespace pcb_deftek.Domain.Contracts;

/// <summary>
/// Input boundary command for updating the custom editable name of an inspection.
/// </summary>
public record UpdateInspectionNameCommand(
    Guid InspectionId,
    string NewName);
