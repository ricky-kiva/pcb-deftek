using System;
using System.Collections.Generic;
using pcb_deftek.Common.Exceptions;

namespace pcb_deftek.Domain.Models;

/// <summary>
/// Aggregate root representing an inspection session on a PCB board.
/// </summary>
public class InspectionRecord
{
    public Guid Id { get; }
    public string Name { get; }
    public DateTime CreatedAtUtc { get; }
    public DateTime UpdatedAtUtc { get; }
    public DetectionMode Mode { get; }
    public int DefectCount => Defects.Count;
    public InspectionStatus Status => DefectCount == 0 ? InspectionStatus.Pass : InspectionStatus.Fail;
    public string ImagePath { get; }
    public IReadOnlyList<DetectedDefect> Defects { get; }

    public InspectionRecord(
        Guid id,
        string name,
        DateTime createdAtUtc,
        DateTime updatedAtUtc,
        DetectionMode mode,
        string imagePath,
        IReadOnlyList<DetectedDefect>? defects = null)
    {
        if (id == Guid.Empty)
            throw new ValidationException("Inspection ID cannot be empty.", nameof(Id), id);

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        Id = id;
        Name = name.Trim();
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        Mode = mode;
        ImagePath = imagePath.Trim();
        Defects = defects ?? Array.Empty<DetectedDefect>();
    }

    /// <summary>
    /// Creates a new immutable instance of <see cref="InspectionRecord"/> with an updated board name.
    /// </summary>
    public InspectionRecord WithUpdatedName(string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        return new InspectionRecord(Id, newName, CreatedAtUtc, DateTime.UtcNow, Mode, ImagePath, Defects);
    }
}
