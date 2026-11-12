using System.Threading;
using System.Threading.Tasks;

namespace pcb_deftek.Infrastructure.Notifications;

/// <summary>
/// Contract for dispatching non-intrusive desktop toast notifications.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Displays a Windows desktop toast notification.
    /// In headless, test, or unsupported environments, logs diagnostics safely without throwing exceptions.
    /// </summary>
    /// <param name="title">The primary title of the notification.</param>
    /// <param name="message">The body text of the notification.</param>
    /// <param name="cancellationToken">A token to cancel the notification dispatch.</param>
    Task ShowToastAsync(string title, string message, CancellationToken cancellationToken = default);
}
