using System.Threading;
using System.Threading.Tasks;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Infrastructure.Persistence.Repositories;

/// <summary>
/// Contract for retrieving, mutating, and persisting user settings in local storage.
/// </summary>
public interface IUserSettingsService
{
    /// <summary>
    /// Loads the stored user settings, resolving missing or empty entries with system defaults.
    /// </summary>
    Task<UserSettings> LoadSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the updated user settings and broadcasts change notifications via <see cref="CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger"/>.
    /// </summary>
    Task SaveSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default);
}
