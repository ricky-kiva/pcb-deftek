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
    private readonly Action<string, string> _toastDispatcher;

    /// <summary>
    /// Initializes a new instance of <see cref="WindowsNotificationService"/>.
    /// </summary>
    /// <param name="toastDispatcher">Optional custom dispatcher delegate; if null, invokes native Windows toast APIs.</param>
    public WindowsNotificationService(Action<string, string>? toastDispatcher = null)
    {
        _toastDispatcher = toastDispatcher ?? DefaultToastDispatcher;
    }

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
    private static void DefaultToastDispatcher(string title, string message)
    {
        try
        {
            var toastContent = new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .GetToastContent();

            string xmlPayload = toastContent.GetContent();
            TryShowNativeWindowsToast(title, message, xmlPayload);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Desktop notification shell unavailable or threw exception: {Message}", ex.Message);
        }
    }

    private static void TryShowNativeWindowsToast(string title, string message, string xmlPayload)
    {
        try
        {
            var toastNotificationType = Type.GetType("Windows.UI.Notifications.ToastNotification, Windows.UI.Notifications, ContentType=WindowsRuntime");
            var toastManagerType = Type.GetType("Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType=WindowsRuntime");
            var xmlDocType = Type.GetType("Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType=WindowsRuntime");

            if (toastNotificationType != null && toastManagerType != null && xmlDocType != null)
            {
                var xmlDoc = Activator.CreateInstance(xmlDocType);
                var loadXmlMethod = xmlDocType.GetMethod("LoadXml", new[] { typeof(string) });
                loadXmlMethod?.Invoke(xmlDoc, new object[] { xmlPayload });

                var toast = Activator.CreateInstance(toastNotificationType, xmlDoc);
                var createNotifierMethod = toastManagerType.GetMethod("CreateToastNotifier", Type.EmptyTypes);
                var notifier = createNotifierMethod?.Invoke(null, null);

                var showMethod = notifier?.GetType().GetMethod("Show");
                showMethod?.Invoke(notifier, new[] { toast });
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Native WinRT notification activation unavailable: {Message}", ex.Message);
        }

        Log.Information("Toast notification dispatched: [{Title}] {Message}", title, message);
    }
}
