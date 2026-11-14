using System;
using System.Globalization;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Presentation.ViewModels;

/// <summary>
/// Aggregated defect count and highest confidence for one defect class.
/// </summary>
public sealed record DefectClassSummaryViewModel(
    DefectClassInfo ClassInfo,
    int Count,
    float HighestConfidence)
{
    /// <summary>Gets the class display name.</summary>
    public string DisplayName => ClassInfo.DisplayName;

    /// <summary>Gets the class abbreviation.</summary>
    public string Abbreviation => ClassInfo.Abbreviation;

    /// <summary>Gets the class palette color as <c>#RRGGBB</c>.</summary>
    public string ColorHex => ClassInfo.ColorHex;

    /// <summary>Gets the highest confidence formatted as a whole percentage.</summary>
    public string HighestConfidenceText =>
        string.Create(CultureInfo.InvariantCulture, $"{HighestConfidence * 100:0}%");
}
