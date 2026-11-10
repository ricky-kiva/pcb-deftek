using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using pcb_deftek.Common.Exceptions;

namespace pcb_deftek.Infrastructure.Persistence.Repositories;

/// <summary>
/// SQLite implementation of <see cref="ISettingsRepository"/> providing strongly-typed JSON and primitive setting serialization.
/// </summary>
public class SqliteSettingsRepository : ISettingsRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public SqliteSettingsRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <inheritdoc />
    public async Task<T?> GetSettingAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        const string sql = "SELECT Value FROM AppSettings WHERE Key = @Key;";
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Key", key.Trim());

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result == null || result is DBNull)
        {
            return default;
        }

        string rawValue = Convert.ToString(result)!;

        // If T is string, return direct text
        if (typeof(T) == typeof(string))
        {
            return (T)(object)rawValue;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(rawValue, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"Failed deserializing configuration setting '{key}' into type '{typeof(T).Name}'.", key, ex);
        }
    }

    /// <inheritdoc />
    public async Task SetSettingAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        string serializedValue = typeof(T) == typeof(string)
            ? (value?.ToString() ?? string.Empty)
            : JsonSerializer.Serialize(value, JsonOptions);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        const string upsertSql = @"
            INSERT INTO AppSettings (Key, Value, UpdatedAtUtc)
            VALUES (@Key, @Value, @UpdatedAtUtc)
            ON CONFLICT(Key) DO UPDATE SET
                Value = excluded.Value,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
        ";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = upsertSql;
        cmd.Parameters.AddWithValue("@Key", key.Trim());
        cmd.Parameters.AddWithValue("@Value", serializedValue);
        cmd.Parameters.AddWithValue("@UpdatedAtUtc", DateTime.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> GetAllSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        const string sql = "SELECT Key, Value FROM AppSettings ORDER BY Key ASC;";
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;

        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string k = reader.GetString(0);
            string v = reader.GetString(1);
            settings[k] = v;
        }

        return settings;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        const string deleteSql = "DELETE FROM AppSettings WHERE Key = @Key;";
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = deleteSql;
        cmd.Parameters.AddWithValue("@Key", key.Trim());

        int rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return rows > 0;
    }
}
