using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Toolkit.Uwp.Notifications;
using Serilog;

namespace pcb_deftek.Infrastructure.Notifications;

/// <summary>
/// Windows desktop implementation of <see cref="INotificationService"/> utilizing
/// <see cref="ToastContentBuilder"/> with graceful fallback for headless CI and test environments.
/// </summary>
public class WindowsNotificationService : INotificationService
{
    /// <summary>
    /// Compat dispatcher shared by every default-constructed instance.
    /// </summary>
    private static readonly Action<string, string> CompatToastDispatcher = ShowCompatToast;

    private readonly Action<string, string> _toastDispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsNotificationService"/> class.
    /// </summary>
    /// <param name="toastDispatcher">Optional custom dispatcher delegate; if null, uses the compat toast notifier.</param>
    public WindowsNotificationService(Action<string, string>? toastDispatcher = null)
    {
        _toastDispatcher = toastDispatcher ?? CompatToastDispatcher;
    }

    /// <summary>
    /// Gets a value indicating whether this instance dispatches through the compat toast notifier
    /// that registers an AppUserModelID, rather than an injected test delegate.
    /// </summary>
    internal bool UsesCompatDispatcher => ReferenceEquals(_toastDispatcher, CompatToastDispatcher);

    /// <inheritdoc />
    public async Task ShowToastAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        cancellationToken.ThrowIfCancellationRequested();

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _toastDispatcher(title, message);
                Log.Information("Toast notification dispatched: [{Title}] {Message}", title, message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Warning(ex, "Windows toast notification could not be presented natively; logging fallback: [{Title}] {Message}",
                    title, message);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Shows a toast through <see cref="ToastNotificationManagerCompat"/>. On first use it registers
    /// an AppUserModelID and a Start-menu shortcut, which is what makes toasts work for unpackaged
    /// processes (Visual Studio debugging, <c>dotnet run</c>, portable publish).
    /// </summary>
    private static void ShowCompatToast(string title, string message)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .Show();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Desktop notification shell unavailable or threw exception: {Message}", ex.Message);
        }
    }
}
