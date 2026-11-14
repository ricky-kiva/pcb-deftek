using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace pcb_deftek.Presentation.Services;

/// <summary>
/// Thread-safe observable progress source. Updates are marshalled through the captured
/// UI synchronization context so WPF bindings are notified on the dispatcher thread.
/// </summary>
public sealed class DashboardProgressService : ObservableObject, IDashboardProgressService
{
    private readonly SynchronizationContext? _uiContext;
    private DashboardProgressState _inference = DashboardProgressState.Idle;
    private DashboardProgressState _dataExport = DashboardProgressState.Idle;

    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardProgressService"/> class.
    /// </summary>
    /// <param name="uiContext">UI context to marshal updates through; defaults to the current context.</param>
    public DashboardProgressService(SynchronizationContext? uiContext = null)
    {
        _uiContext = uiContext ?? SynchronizationContext.Current;
    }

    /// <inheritdoc />
    public DashboardProgressState Inference
    {
        get => _inference;
        private set => SetProperty(ref _inference, value);
    }

    /// <inheritdoc />
    public DashboardProgressState DataExport
    {
        get => _dataExport;
        private set => SetProperty(ref _dataExport, value);
    }

    /// <inheritdoc />
    public void ReportInference(DashboardProgressStatus status, double percentage, string? message = null) =>
        Dispatch(() => Inference = CreateState(status, percentage, message));

    /// <inheritdoc />
    public void ReportDataExport(DashboardProgressStatus status, double percentage, string? message = null) =>
        Dispatch(() => DataExport = CreateState(status, percentage, message));

    /// <inheritdoc />
    public void Reset() => Dispatch(() =>
    {
        Inference = DashboardProgressState.Idle;
        DataExport = DashboardProgressState.Idle;
    });

    private static DashboardProgressState CreateState(
        DashboardProgressStatus status,
        double percentage,
        string? message)
    {
        double clamped = Math.Clamp(percentage, 0, 100);
        string text = string.IsNullOrWhiteSpace(message)
            ? status.ToString()
            : message.Trim();
        return new DashboardProgressState(status, clamped, text);
    }

    private void Dispatch(Action action)
    {
        if (_uiContext is null || ReferenceEquals(SynchronizationContext.Current, _uiContext))
        {
            action();
            return;
        }

        _uiContext.Send(_ => action(), null);
    }
}
