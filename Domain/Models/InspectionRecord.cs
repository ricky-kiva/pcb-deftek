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
    public bool IsPass => Status == InspectionStatus.Pass;
    public bool HasDefects => DefectCount > 0;
    public string ImagePath { get; }
    public IReadOnlyList<DetectedDefect> Defects { get; }
    public InspectionOrigin Origin { get; }
    public InspectionRecord(
        Guid id,
        string name,
        DateTime createdAtUtc,
        DateTime updatedAtUtc,
        DetectionMode mode,
        string imagePath,
        IReadOnlyList<DetectedDefect>? defects = null,
        InspectionOrigin origin = InspectionOrigin.External)
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
        Origin = origin;
    }
    /// <summary>
    /// Factory method for creating a newly initiated <see cref="InspectionRecord"/> with server-generated fields.
    /// </summary>
    public static InspectionRecord Create(
        string name,
        DetectionMode mode,
        string imagePath,
        IReadOnlyList<DetectedDefect>? defects = null,
        Guid? id = null,
        DateTime? createdAtUtc = null,
        InspectionOrigin origin = InspectionOrigin.External)
    {
        var timestamp = createdAtUtc ?? DateTime.UtcNow;
        return new InspectionRecord(
            id: id ?? Guid.NewGuid(),
            name: name,
            createdAtUtc: timestamp,
            updatedAtUtc: timestamp,
            mode: mode,
            imagePath: imagePath,
            defects: defects,
            origin: origin);
    }

    /// <summary>
    /// Creates a new immutable instance of <see cref="InspectionRecord"/> with an updated board name.
    /// </summary>
    public InspectionRecord WithUpdatedName(string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        return new InspectionRecord(Id, newName, CreatedAtUtc, DateTime.UtcNow, Mode, ImagePath, Defects, Origin);
    }
}
