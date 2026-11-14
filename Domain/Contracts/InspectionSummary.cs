using System;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Domain.Contracts;

/// <summary>
/// Lightweight read projection of an inspection record used by history galleries.
/// Carries the persisted defect count so status badges are accurate without loading defect rows.
/// </summary>
/// <param name="Id">Unique inspection identifier.</param>
/// <param name="Name">User-assigned inspection name.</param>
/// <param name="CreatedAtUtc">UTC timestamp when the inspection was created.</param>
/// <param name="UpdatedAtUtc">UTC timestamp when the inspection was last updated.</param>
/// <param name="Mode">Capture modality that produced the inspection.</param>
/// <param name="DefectCount">Persisted number of detected defects.</param>
/// <param name="ImagePath">Path to the stored raw inspection image.</param>
public sealed record InspectionSummary(
    Guid Id,
    string Name,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DetectionMode Mode,
    int DefectCount,
    string ImagePath,
    InspectionOrigin Origin = InspectionOrigin.External)
{
    /// <summary>
    /// Gets the PASS/FAIL status derived from the persisted defect count.
    /// </summary>
    public InspectionStatus Status => DefectCount == 0 ? InspectionStatus.Pass : InspectionStatus.Fail;

    /// <summary>
    /// Gets a value indicating whether the inspection has no detected defects.
    /// </summary>
    public bool IsPass => Status == InspectionStatus.Pass;
}
