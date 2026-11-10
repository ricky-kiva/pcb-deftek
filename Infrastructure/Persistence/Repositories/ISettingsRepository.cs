using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace pcb_deftek.Infrastructure.Persistence.Repositories;

/// <summary>
/// Contract for storing and retrieving strongly typed application configuration settings.
/// </summary>
public interface ISettingsRepository
{
    /// <summary>
    /// Retrieves a setting by key, deserializing JSON or converting primitives to type <typeparamref name="T"/>.
    /// Returns default(T) if the key is not found.
    /// </summary>
    Task<T?> GetSettingAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists or updates a setting by key, serializing objects to JSON format.
    /// </summary>
    Task SetSettingAsync<T>(string key, T value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all raw stored key-value setting entries.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetAllSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a setting by key. Returns true if removed, false if not found.
    /// </summary>
    Task<bool> DeleteSettingAsync(string key, CancellationToken cancellationToken = default);
}
