using System.Threading;
using System.Threading.Tasks;

namespace pcb_deftek.Presentation.Services;

/// <summary>
/// Presents destructive-action confirmations to the operator. Abstracted so view models
/// can be tested without opening modal UI.
/// </summary>
public interface IConfirmationService
{
    /// <summary>
    /// Shows a confirmation prompt and returns whether the operator explicitly confirmed.
    /// The default/cancel action must be non-destructive.
    /// </summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="message">Prompt body describing the consequences and retention policy.</param>
    /// <param name="confirmText">Short action label included in the prompt (e.g. "Delete").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> only when the operator confirms; otherwise <see langword="false"/>.</returns>
    Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText = "Delete",
        CancellationToken cancellationToken = default);
}
