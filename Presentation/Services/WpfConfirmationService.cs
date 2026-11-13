using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace pcb_deftek.Presentation.Services;

/// <summary>
/// Native WPF implementation of <see cref="IConfirmationService"/>.
/// Uses a Yes/No modal dialog owned by the main window, defaulting to "No" (cancel).
/// </summary>
public sealed class WpfConfirmationService : IConfirmationService
{
    /// <inheritdoc />
    public Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText = "Delete",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        cancellationToken.ThrowIfCancellationRequested();

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            return dispatcher.InvokeAsync(() => ShowDialog(title, message, confirmText)).Task;
        }

        return Task.FromResult(ShowDialog(title, message, confirmText));
    }

    private static bool ShowDialog(string title, string message, string confirmText)
    {
        // No application host (e.g. headless tests): fail closed rather than showing a stray dialog.
        if (Application.Current is null)
        {
            return false;
        }

        string action = string.IsNullOrWhiteSpace(confirmText)
            ? "continue"
            : confirmText.Trim().TrimEnd('.').ToLowerInvariant();

        string prompt = $"{message}\n\nSelect 'Yes' to {action}.";

        var owner = Application.Current.MainWindow;
        MessageBoxResult result = owner is null
            ? MessageBox.Show(
                prompt,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No)
            : MessageBox.Show(
                owner,
                prompt,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }
}
