using System;

namespace pcb_deftek.Presentation.Models;

/// <summary>
/// Event payload for an inspection rename that was already persisted.
/// </summary>
/// <param name="InspectionId">Identifier of the renamed inspection.</param>
/// <param name="Name">The persisted, trimmed name.</param>
public sealed record InspectionRenamedEventArgs(Guid InspectionId, string Name);
