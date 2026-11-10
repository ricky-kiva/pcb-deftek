using System;
using System.Collections.Generic;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Vision;

namespace pcb_deftek.Infrastructure.Inference;

/// <summary>
/// The unified result of an end-to-end PCB defect detection pass, including detected defects,
/// geometric letterbox transformation, active hardware execution device, and diagnostic latency metrics.
/// </summary>
public record InferenceResult(
    Guid InspectionId,
    IReadOnlyList<DetectedDefect> Defects,
    LetterboxTransform Transform,
    ExecutionDevice Device,
    InferenceTiming Timing)
{
    /// <summary>
    /// Gets the total defect count detected on the board.
    /// </summary>
    public int DefectCount => Defects.Count;

    /// <summary>
    /// Evaluates whether the PCB passed inspection (true if 0 defects detected; otherwise false).
    /// </summary>
    public bool Passed => Defects.Count == 0;

    /// <summary>
    /// Gets the evaluated inspection status (<see cref="InspectionStatus.Pass"/> if 0 defects, otherwise <see cref="InspectionStatus.Fail"/>).
    /// </summary>
    public InspectionStatus Status => Passed ? InspectionStatus.Pass : InspectionStatus.Fail;
}
