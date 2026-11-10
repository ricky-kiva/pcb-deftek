using System;
using pcb_deftek.Common.Exceptions;

namespace pcb_deftek.Domain.Models;

/// <summary>
/// Represents a localized defect identified on a PCB board with model confidence,
/// spatial coordinates, and dynamic diagnostic knowledge base delegation.
/// </summary>
public class DetectedDefect
{
    public Guid Id { get; }
    public Guid InspectionId { get; }
    public string ClassCode { get; }
    public float Confidence { get; }
    public BoundingBox BoundingBox { get; }

    /// <summary>
    /// Gets the authoritative diagnostic metadata resolved dynamically from the static knowledge base.
    /// </summary>
    public DefectClassInfo ClassInfo => DefectKnowledgeBase.GetByCode(ClassCode);

    public string ClassName => ClassInfo.DisplayName;
    public string Explanation => ClassInfo.Explanation;
    public DefectSeverity Severity => ClassInfo.Severity;
    public string RootCause => ClassInfo.RootCause;
    public string Impact => ClassInfo.Impact;
    public string Action => ClassInfo.Action;

    public DetectedDefect(Guid id, Guid inspectionId, string classCode, float confidence, BoundingBox boundingBox)
    {
        if (id == Guid.Empty)
            throw new ValidationException("Defect ID cannot be empty.", nameof(Id), id);

        if (inspectionId == Guid.Empty)
            throw new ValidationException("Inspection ID cannot be empty.", nameof(InspectionId), inspectionId);

        ArgumentException.ThrowIfNullOrWhiteSpace(classCode);

        if (float.IsNaN(confidence) || confidence < 0.0f || confidence > 1.0f)
            throw new ValidationException("Confidence score must be normalized between 0.0 and 1.0.", nameof(Confidence), confidence);

        Id = id;
        InspectionId = inspectionId;
        ClassCode = classCode.Trim();
        Confidence = confidence;
        BoundingBox = boundingBox;
    }
}
