using System;
using pcb_deftek.Domain.Models;
using pcb_deftek.Presentation.Models;

namespace pcb_deftek.Presentation.ViewModels;

/// <summary>
/// Immutable presentation data for one defect knowledge accordion item.
/// </summary>
public sealed class DefectInfoItemViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DefectInfoItemViewModel"/> class.
    /// </summary>
    public DefectInfoItemViewModel(DefectClassInfo info)
    {
        Info = info ?? throw new ArgumentNullException(nameof(info));
    }

    /// <summary>Gets the source knowledge-base entry.</summary>
    public DefectClassInfo Info { get; }

    /// <summary>Gets the defect class.</summary>
    public DefectClass Class => Info.Class;

    /// <summary>Gets the taxonomy order index.</summary>
    public int ClassId => Info.ClassId;

    /// <summary>Gets the display name.</summary>
    public string DisplayName => Info.DisplayName;

    /// <summary>Gets the short abbreviation.</summary>
    public string Abbreviation => Info.Abbreviation;

    /// <summary>Gets the severity.</summary>
    public DefectSeverity Severity => Info.Severity;

    /// <summary>Gets the display severity label.</summary>
    public string SeverityLabel => DefectSeverityLabels.Get(Info.Severity);

    /// <summary>Gets the explanation text.</summary>
    public string Explanation => Info.Explanation;

    /// <summary>Gets the root-cause guidance.</summary>
    public string RootCause => Info.RootCause;

    /// <summary>Gets the impact guidance.</summary>
    public string Impact => Info.Impact;

    /// <summary>Gets the recommended action.</summary>
    public string Action => Info.Action;

    /// <summary>Gets the single-source opaque color.</summary>
    public string ColorHex => Info.ColorHex;

    /// <summary>
    /// Gets the repository-relative knowledge image path.
    /// </summary>
    public string ImagePath => $"Assets/Images/dashb-info-{Info.Abbreviation.ToLowerInvariant()}.png";

}
