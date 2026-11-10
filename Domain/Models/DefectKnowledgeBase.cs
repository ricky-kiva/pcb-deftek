using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace pcb_deftek.Domain.Models;

/// <summary>
/// Authoritative centralized static knowledge base containing comprehensive diagnostic metadata
/// and remediation guidance for all 9 PCB defect classes.
/// </summary>
public static class DefectKnowledgeBase
{
    private static readonly Dictionary<DefectClass, DefectClassInfo> InfoByClass;
    private static readonly Dictionary<string, DefectClassInfo> InfoByCode;
    private static readonly Dictionary<int, DefectClassInfo> InfoByIndex;
    private static readonly DefectClassInfo[] DefinitionsList;

    /// <summary>
    /// Gets all registered defect class definitions indexed by <see cref="DefectClass"/>.
    /// </summary>
    public static IReadOnlyDictionary<DefectClass, DefectClassInfo> All { get; }
    static DefectKnowledgeBase()
    {
        var definitions = new[]
        {
            new DefectClassInfo(
                Class: DefectClass.Short,
                ClassId: 0,
                Code: "short",
                DisplayName: "Short",
                Explanation: "An unintended connection between two or more conductors caused by residual copper.",
                Severity: DefectSeverity.Critical,
                RootCause: "Etching under-exposure, foreign conductive debris, or design/manufacturing tolerance violation.",
                Impact: "Can cause immediate circuit malfunction, component damage, or safety hazards when powered.",
                Action: "Immediate rework or scrap — do not power on the board until the short is physically removed and re-verified."
            ),
            new DefectClassInfo(
                Class: DefectClass.Spur,
                ClassId: 1,
                Code: "spur",
                DisplayName: "Spur",
                Explanation: "An irregular, sharp, pointed protrusion along a conductor's edge.",
                Severity: DefectSeverity.Medium,
                RootCause: "Etching resist imperfection or artwork/photomask defect during PCB fabrication.",
                Impact: "May grow into a short circuit if it contacts a neighboring conductor, especially under vibration or thermal expansion.",
                Action: "Trim or verify clearance to nearest conductor meets minimum spacing rules; rework if clearance is violated."
            ),
            new DefectClassInfo(
                Class: DefectClass.SpuriousCopper,
                ClassId: 2,
                Code: "spurious_copper",
                DisplayName: "Spurious copper",
                Explanation: "Irregular or unwanted copper residue found on base material, within holes, or on copper surfaces.",
                Severity: DefectSeverity.MediumHigh,
                RootCause: "Incomplete etching during the copper removal process, often due to uneven etchant flow or resist defects.",
                Impact: "Can create unintended electrical bridges between adjacent traces, risking short circuits under operating voltage.",
                Action: "Re-inspect under magnification; if confirmed, route board for rework/re-etch or scrap depending on severity and location."
            ),
            new DefectClassInfo(
                Class: DefectClass.Open,
                ClassId: 3,
                Code: "open",
                DisplayName: "Open",
                Explanation: "A break in a conductor's path, interrupting the flow of electric current.",
                Severity: DefectSeverity.High,
                RootCause: "Etching over-exposure, contamination during lamination, or physical damage during handling.",
                Impact: "The connected circuit will not function at all — this is a functional (not just cosmetic) defect.",
                Action: "Immediate rework required (trace repair/jumper) or board rejection; do not pass to next assembly stage untested."
            ),
            new DefectClassInfo(
                Class: DefectClass.MouseBite,
                ClassId: 4,
                Code: "mouse_bite",
                DisplayName: "Mouse bite",
                Explanation: "A small, localized depression or crack at the edge of a conductor.",
                Severity: DefectSeverity.LowMedium,
                RootCause: "Mechanical stress during depaneling (routing/punching) breaking off small fragments of copper/substrate.",
                Impact: "May result in poor connections or connections close to being interrupted, weakening reliability over time.",
                Action: "Measure remaining trace width against design minimum; approve if within tolerance, otherwise flag for engineering review."
            ),
            new DefectClassInfo(
                Class: DefectClass.HoleBreakout,
                ClassId: 5,
                Code: "hole_breakout",
                DisplayName: "Hole breakout",
                Explanation: "A situation where a drilled hole's center significantly deviates from its bounding pad, causing missing copper along part of the hole's edge.",
                Severity: DefectSeverity.Medium,
                RootCause: "Drilling misalignment or registration error during multi-layer lamination/drilling.",
                Impact: "Affects functionality and performance of the hole, especially in multi-layer PCBs where holes connect different circuit layers.",
                Action: "Assess breakout severity against pad annular ring requirements; minor cases may pass, larger ones require rework or scrap."
            ),
            new DefectClassInfo(
                Class: DefectClass.ConductorScratch,
                ClassId: 6,
                Code: "conductor_scratch",
                DisplayName: "Conductor scratch",
                Explanation: "A linear or multi-linear scratch defect on copper wires or copper surfaces.",
                Severity: DefectSeverity.Medium,
                RootCause: "Mechanical abrasion during handling, transport, or automated processing equipment contact.",
                Impact: "Can result in suboptimal or interrupted electrical connections and reduced mechanical strength, increasing vulnerability to vibration, impact, or thermal cycling.",
                Action: "Inspect scratch depth and whether it fully severs the conductor; approve shallow scratches, rework or scrap deep/severing ones."
            ),
            new DefectClassInfo(
                Class: DefectClass.ConductorForeignObject,
                ClassId: 7,
                Code: "conductor_foreign_object",
                DisplayName: "Conductor foreign object",
                Explanation: "Impurities, bubbles, dirt, deposits, or contamination (oils, oxides, chemical residue, fingerprints) present on a conductor.",
                Severity: DefectSeverity.Medium,
                RootCause: "Inadequate cleaning after etching/plating, or contamination introduced during handling.",
                Impact: "Can obstruct connections between circuit elements, interfere with circuit signals, or cause shorts between components that should not be connected.",
                Action: "Clean and re-inspect; if contamination persists or affects conductivity, route for rework."
            ),
            new DefectClassInfo(
                Class: DefectClass.BaseMaterialForeignObject,
                ClassId: 8,
                Code: "base_material_foreign_object",
                DisplayName: "Base material foreign object",
                Explanation: "Foreign material (bubbles, chips, particles, grease, adhesives, oxidation) present on the base material or inside holes.",
                Severity: DefectSeverity.LowMedium,
                RootCause: "Contamination during fabrication, lamination, or handling of the bare substrate.",
                Impact: "Can obstruct connections, interfere with circuit signals, or cause shorts if conductive; generally less severe than conductor-based defects.",
                Action: "Clean and re-inspect; assess whether contamination is conductive before deciding on rework vs. pass."
            )
        };

        InfoByClass = new Dictionary<DefectClass, DefectClassInfo>();
        InfoByCode = new Dictionary<string, DefectClassInfo>(StringComparer.OrdinalIgnoreCase);
        InfoByIndex = new Dictionary<int, DefectClassInfo>();

        foreach (var def in definitions)
        {
            InfoByClass[def.Class] = def;
            InfoByCode[def.Code] = def;
            InfoByIndex[def.ClassId] = def;
        }

        DefinitionsList = definitions;
        All = new ReadOnlyDictionary<DefectClass, DefectClassInfo>(InfoByClass);
    }

    /// <summary>
    /// Gets diagnostic information for the specified <see cref="DefectClass"/>.
    /// </summary>
    public static DefectClassInfo GetInfo(DefectClass defectClass)
    {
        if (TryGetInfo(defectClass, out var info))
        {
            return info;
        }

        throw new KeyNotFoundException($"Defect class '{defectClass}' is not registered in the knowledge base.");
    }

    /// <summary>
    /// Attempts to retrieve diagnostic information for the specified <see cref="DefectClass"/>.
    /// </summary>
    public static bool TryGetInfo(DefectClass defectClass, [NotNullWhen(true)] out DefectClassInfo? info) =>
        InfoByClass.TryGetValue(defectClass, out info);

    /// <summary>
    /// Gets diagnostic information for the specified snake_case class code.
    /// </summary>
    public static DefectClassInfo GetByCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        if (TryGetByCode(code, out var info))
        {
            return info!;
        }

        throw new KeyNotFoundException($"Defect code '{code}' is not registered in the knowledge base.");
    }
    /// <summary>
    /// Attempts to retrieve diagnostic information for the specified snake_case class code.
    /// </summary>
    public static bool TryGetByCode(string? code, [NotNullWhen(true)] out DefectClassInfo? info)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            info = null;
            return false;
        }

        return InfoByCode.TryGetValue(code.Trim(), out info);
    }
    /// <summary>
    /// Determines whether the specified code string corresponds to a recognized defect class.
    /// </summary>
    public static bool IsValidCode(string? code) =>
        !string.IsNullOrWhiteSpace(code) && InfoByCode.ContainsKey(code.Trim());

    /// <summary>
    /// Gets diagnostic information for the specified zero-based model class index (0 to 8).
    /// </summary>
    public static DefectClassInfo GetByIndex(int classId)
    {
        if (TryGetByIndex(classId, out var info))
        {
            return info!;
        }

        throw new ArgumentOutOfRangeException(nameof(classId), classId, "Defect class index must be between 0 and 8.");
    }
    /// <summary>
    /// Attempts to retrieve diagnostic information for the specified model class index.
    /// </summary>
    public static bool TryGetByIndex(int classId, [NotNullWhen(true)] out DefectClassInfo? info) =>
        InfoByIndex.TryGetValue(classId, out info);
    /// <summary>
    /// Gets all registered defect definitions ordered by sequential class index.
    /// </summary>
    public static IReadOnlyList<DefectClassInfo> GetAll() =>
        DefinitionsList;
}
