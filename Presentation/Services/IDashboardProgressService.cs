using System.ComponentModel;
using System.Threading;

namespace pcb_deftek.Presentation.Services;

/// <summary>
/// Identity of one dashboard background process.
/// </summary>
public enum DashboardProcess
{
    Inference = 0,
    DataExport = 1
}

/// <summary>
/// Lifecycle state for one dashboard background operation.
/// </summary>
public enum DashboardProgressStatus
{
    Idle = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

/// <summary>
/// Immutable progress snapshot for one dashboard operation.
/// </summary>
public sealed record DashboardProgressState(
    DashboardProgressStatus Status,
    double Percentage,
    string Message)
{
    /// <summary>Gets the idle state at zero percent with no message.</summary>
    public static DashboardProgressState Idle { get; } = new(
        DashboardProgressStatus.Idle,
        0,
        string.Empty);

    /// <summary>Gets a value indicating whether the process is idle.</summary>
    public bool IsIdle => Status == DashboardProgressStatus.Idle;

    /// <summary>Gets a value indicating whether the process is running.</summary>
    public bool IsRunning => Status == DashboardProgressStatus.Running;

    /// <summary>Gets a value indicating whether there is a message to show beside the status.</summary>
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
}

/// <summary>
/// Observable progress and cancellation source for the inference and data-export processes
/// shown by the dashboard Background Activity modal.
/// </summary>
public interface IDashboardProgressService : INotifyPropertyChanged
{
    /// <summary>Gets the current inference progress.</summary>
    DashboardProgressState Inference { get; }

    /// <summary>Gets the current data-export progress.</summary>
    DashboardProgressState DataExport { get; }

    /// <summary>Gets a value indicating whether at least one process is running.</summary>
    bool HasActiveProcesses { get; }

    /// <summary>
    /// Gets the cancellation token for the process's current run. The token is stable for one
    /// run and replaced after completion, failure, cancellation, or reset.
    /// </summary>
    /// <param name="process">Process identity.</param>
    /// <returns>The current run's cancellation token.</returns>
    CancellationToken GetCancellationToken(DashboardProcess process);

    /// <summary>Reports inference progress, clamping the percentage to 0–100.</summary>
    void ReportInference(DashboardProgressStatus status, double percentage, string? message = null);

    /// <summary>Reports data-export progress, clamping the percentage to 0–100.</summary>
    void ReportDataExport(DashboardProgressStatus status, double percentage, string? message = null);

    /// <summary>
    /// Requests cancellation of a running process. Cancelling an idle or completed process is a no-op.
    /// </summary>
    /// <param name="process">Process identity.</param>
    void Cancel(DashboardProcess process);

    /// <summary>Cancels every running process; used during application shutdown.</summary>
    void CancelAll();

    /// <summary>Returns both operations to a fresh idle state and replaces their tokens.</summary>
    void Reset();
}
