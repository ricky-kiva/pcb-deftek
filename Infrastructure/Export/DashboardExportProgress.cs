using System;

namespace pcb_deftek.Infrastructure.Export;

/// <summary>
/// Intermediate progress reported by an export. The percentage is clamped to 0–100 and the
/// message is never null; the view model maps these updates onto its process state.
/// </summary>
public sealed record DashboardExportProgress
{
    /// <summary>Initializes a new instance of the <see cref="DashboardExportProgress"/> class.</summary>
    /// <param name="percentage">Completion percentage; clamped to 0–100.</param>
    /// <param name="message">Optional progress message.</param>
    public DashboardExportProgress(double percentage, string? message = null)
    {
        if (!double.IsFinite(percentage))
        {
            throw new ArgumentOutOfRangeException(
                nameof(percentage),
                percentage,
                "Progress percentage must be a finite number.");
        }

        Percentage = Math.Clamp(percentage, 0.0, 100.0);
        Message = message?.Trim() ?? string.Empty;
    }

    /// <summary>Gets the clamped completion percentage.</summary>
    public double Percentage { get; }

    /// <summary>Gets the trimmed progress message, or an empty string.</summary>
    public string Message { get; }
}
