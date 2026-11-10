using pcb_deftek.Domain.Models;

namespace pcb_deftek.Domain.Contracts;

/// <summary>
/// Input boundary payload representing an individual defect detected during model inference.
/// </summary>
public record CreateDefectInput(
    string ClassCode,
    float Confidence,
    BoundingBox BoundingBox);
