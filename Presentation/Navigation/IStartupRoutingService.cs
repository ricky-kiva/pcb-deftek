using System.Threading;
using System.Threading.Tasks;

namespace pcb_deftek.Presentation.Navigation;

/// <summary>
/// Contract for evaluating persisted user startup preferences and routing to the initial application page.
/// </summary>
public interface IStartupRoutingService
{
    /// <summary>
    /// Evaluates user preferences ("Skip dashboard on startup" or default startup page)
    /// and navigates the application to the corresponding initial view model.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task RouteStartupAsync(CancellationToken cancellationToken = default);
}
