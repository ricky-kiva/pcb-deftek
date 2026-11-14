using System.ComponentModel;

namespace pcb_deftek.Presentation.Services;

/// <summary>
/// Lifecycle state for one dashboard background operation.
/// </summary>
public enum DashboardProgressStatus
{
    Idle = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}

/// <summary>
/// Immutable progress snapshot for one dashboard operation.
/// </summary>
public sealed record DashboardProgressState(
    DashboardProgressStatus Status,
    double Percentage,
    string Message)
{
    /// <summary>Gets the idle state at zero percent.</summary>
    public static DashboardProgressState Idle { get; } = new(
        DashboardProgressStatus.Idle,
        0,
        nameof(DashboardProgressStatus.Idle));
}

/// <summary>
/// Observable progress source for inference and data-export operations shown by the
/// dashboard notification modal.
/// </summary>
public interface IDashboardProgressService : INotifyPropertyChanged
{
    /// <summary>Gets the current inference progress.</summary>
    DashboardProgressState Inference { get; }

    /// <summary>Gets the current data-export progress.</summary>
    DashboardProgressState DataExport { get; }

    /// <summary>Reports inference progress, clamping the percentage to 0–100.</summary>
    void ReportInference(DashboardProgressStatus status, double percentage, string? message = null);

    /// <summary>Reports data-export progress, clamping the percentage to 0–100.</summary>
    void ReportDataExport(DashboardProgressStatus status, double percentage, string? message = null);

    /// <summary>Returns both operations to the idle state.</summary>
    void Reset();
}
