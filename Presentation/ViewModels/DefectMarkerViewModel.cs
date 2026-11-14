using System;
using System.Globalization;
using System.Windows;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Presentation.ViewModels;

/// <summary>
/// Immutable view model for one defect bounding box in the interactive viewer.
/// </summary>
public sealed class DefectMarkerViewModel
{
    public DefectMarkerViewModel(DetectedDefect defect, Rect pixelBounds)
    {
        Defect = defect ?? throw new ArgumentNullException(nameof(defect));
        PixelBounds = pixelBounds;
    }

    /// <summary>Gets the underlying defect.</summary>
    public DetectedDefect Defect { get; }

    /// <summary>Gets the defect identifier.</summary>
    public Guid Id => Defect.Id;

    /// <summary>Gets the normalized bounding box.</summary>
    public BoundingBox NormalizedBounds => Defect.BoundingBox;

    /// <summary>Gets the pixel-aligned bounding box for the loaded image.</summary>
    public Rect PixelBounds { get; }

    /// <summary>Gets the defect class metadata.</summary>
    public DefectClassInfo ClassInfo => Defect.ClassInfo;

    /// <summary>Gets the class display name.</summary>
    public string ClassName => ClassInfo.DisplayName;

    /// <summary>Gets the class abbreviation.</summary>
    public string Abbreviation => ClassInfo.Abbreviation;

    /// <summary>Gets the class palette color as <c>#RRGGBB</c>.</summary>
    public string ColorHex => ClassInfo.ColorHex;

    /// <summary>Gets the confidence formatted as a whole percentage.</summary>
    public string ConfidenceText =>
        string.Create(CultureInfo.InvariantCulture, $"{Defect.Confidence * 100:0}%");
}
