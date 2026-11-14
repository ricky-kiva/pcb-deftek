using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace pcb_deftek.Presentation.Services;

/// <summary>
/// Thread-safe observable progress source. Updates are marshalled through the captured
/// UI synchronization context so WPF bindings are notified on the dispatcher thread.
/// Each process owns a cancellation token source that is replaced after a run ends.
/// </summary>
public sealed class DashboardProgressService : ObservableObject, IDashboardProgressService
{
    private readonly SynchronizationContext? _uiContext;
    private readonly object _tokenGate = new();
    private CancellationTokenSource _inferenceTokenSource = new();
    private CancellationTokenSource _dataExportTokenSource = new();
    private DashboardProgressState _inference = DashboardProgressState.Idle;
    private DashboardProgressState _dataExport = DashboardProgressState.Idle;
    private bool _hasActiveProcesses;

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
        private set
        {
            if (SetProperty(ref _inference, value))
            {
                RefreshHasActiveProcesses();
            }
        }
    }

    /// <inheritdoc />
    public DashboardProgressState DataExport
    {
        get => _dataExport;
        private set
        {
            if (SetProperty(ref _dataExport, value))
            {
                RefreshHasActiveProcesses();
            }
        }
    }

    /// <inheritdoc />
    public bool HasActiveProcesses => _hasActiveProcesses;

    /// <inheritdoc />
    public CancellationToken GetCancellationToken(DashboardProcess process)
    {
        lock (_tokenGate)
        {
            return GetTokenSource(process).Token;
        }
    }

    /// <inheritdoc />
    public void ReportInference(DashboardProgressStatus status, double percentage, string? message = null) =>
        Report(DashboardProcess.Inference, status, percentage, message);

    /// <inheritdoc />
    public void ReportDataExport(DashboardProgressStatus status, double percentage, string? message = null) =>
        Report(DashboardProcess.DataExport, status, percentage, message);

    /// <inheritdoc />
    public void Cancel(DashboardProcess process)
    {
        DashboardProgressState state = GetState(process);
        if (state.Status != DashboardProgressStatus.Running)
        {
            return;
        }

        CancellationTokenSource tokenSource;
        lock (_tokenGate)
        {
            tokenSource = GetTokenSource(process);
        }

        CancelTokenSource(tokenSource);
        Report(process, DashboardProgressStatus.Cancelled, state.Percentage, null);
    }

    /// <inheritdoc />
    public void CancelAll()
    {
        Cancel(DashboardProcess.Inference);
        Cancel(DashboardProcess.DataExport);
    }

    /// <inheritdoc />
    public void Reset() => Dispatch(() =>
    {
        CancellationTokenSource inference;
        CancellationTokenSource export;
        lock (_tokenGate)
        {
            inference = _inferenceTokenSource;
            export = _dataExportTokenSource;
            _inferenceTokenSource = new CancellationTokenSource();
            _dataExportTokenSource = new CancellationTokenSource();
        }

        CancelTokenSource(inference);
        CancelTokenSource(export);
        Inference = DashboardProgressState.Idle;
        DataExport = DashboardProgressState.Idle;
    });

    private void Report(
        DashboardProcess process,
        DashboardProgressStatus status,
        double percentage,
        string? message)
    {
        Dispatch(() => SetState(process, CreateState(status, percentage, message)));

        if (status is DashboardProgressStatus.Completed
            or DashboardProgressStatus.Failed
            or DashboardProgressStatus.Cancelled)
        {
            RenewToken(process);
        }
    }

    private void RenewToken(DashboardProcess process)
    {
        lock (_tokenGate)
        {
            _ = ReplaceTokenSource(process);
        }
    }

    private CancellationTokenSource ReplaceTokenSource(DashboardProcess process)
    {
        if (process == DashboardProcess.Inference)
        {
            CancellationTokenSource previous = _inferenceTokenSource;
            _inferenceTokenSource = new CancellationTokenSource();
            return previous;
        }

        CancellationTokenSource previousExport = _dataExportTokenSource;
        _dataExportTokenSource = new CancellationTokenSource();
        return previousExport;
    }

    private CancellationTokenSource GetTokenSource(DashboardProcess process) => process switch
    {
        DashboardProcess.Inference => _inferenceTokenSource,
        DashboardProcess.DataExport => _dataExportTokenSource,
        _ => throw new ArgumentOutOfRangeException(nameof(process), process, "Unknown dashboard process.")
    };

    private DashboardProgressState GetState(DashboardProcess process) => process switch
    {
        DashboardProcess.Inference => Inference,
        DashboardProcess.DataExport => DataExport,
        _ => throw new ArgumentOutOfRangeException(nameof(process), process, "Unknown dashboard process.")
    };

    private void SetState(DashboardProcess process, DashboardProgressState state)
    {
        if (process == DashboardProcess.Inference)
        {
            Inference = state;
        }
        else
        {
            DataExport = state;
        }
    }

    private void RefreshHasActiveProcesses()
    {
        bool active = Inference.Status == DashboardProgressStatus.Running ||
            DataExport.Status == DashboardProgressStatus.Running;
        if (active == _hasActiveProcesses)
        {
            return;
        }

        _hasActiveProcesses = active;
        OnPropertyChanged(nameof(HasActiveProcesses));
    }

    private static DashboardProgressState CreateState(
        DashboardProgressStatus status,
        double percentage,
        string? message)
    {
        double clamped = Math.Clamp(percentage, 0, 100);
        string text = string.IsNullOrWhiteSpace(message)
            ? string.Empty
            : message.Trim();
        return new DashboardProgressState(status, clamped, text);
    }

    private static void CancelTokenSource(CancellationTokenSource tokenSource)
    {
        try
        {
            tokenSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A previous reset already disposed the source; cancellation is complete.
        }
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
