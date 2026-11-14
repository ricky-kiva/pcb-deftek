namespace pcb_deftek.Domain.Models;

/// <summary>
/// Describes who created an inspection image, which determines whether the application may delete it.
/// </summary>
public enum InspectionOrigin
{
    /// <summary>Imported from a user-owned file; never deleted by the application.</summary>
    External = 0,

    /// <summary>Captured from the live camera; the app owns the raw and annotated images.</summary>
    LiveCapture = 1,

    /// <summary>Exported from the viewer crop; the app owns the cropped image.</summary>
    Crop = 2
}
