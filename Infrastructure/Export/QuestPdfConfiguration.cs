using QuestPDF.Infrastructure;

namespace pcb_deftek.Infrastructure.Export;

/// <summary>
/// Configures the process-wide QuestPDF license exactly once. Community is valid for this
/// non-profit project; no license key or network call is involved.
/// </summary>
public static class QuestPdfConfiguration
{
    private static readonly object Gate = new();
    private static bool _configured;

    /// <summary>
    /// Sets <see cref="QuestPDF.Settings.License"/> to <see cref="LicenseType.Community"/>.
    /// Safe to call from startup and from tests; later calls are no-ops.
    /// </summary>
    public static void EnsureConfigured()
    {
        if (_configured)
        {
            return;
        }

        lock (Gate)
        {
            if (_configured)
            {
                return;
            }

            QuestPDF.Settings.License = LicenseType.Community;
            _configured = true;
        }
    }
}
