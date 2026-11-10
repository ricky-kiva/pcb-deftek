using pcb_deftek.Domain.Models;

namespace pcb_deftek.Infrastructure.Inference;

/// <summary>
/// Intermediate detection result emitted by the YOLO postprocessor before domain inspection entity binding.
/// </summary>
public record YoloDetection(
    int ClassIndex,
    DefectClass Class,
    string ClassCode,
    string ClassName,
    float Confidence,
    BoundingBox BoundingBox);
