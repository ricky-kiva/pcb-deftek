using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using pcb_deftek.Common.Exceptions;
using Serilog;

namespace pcb_deftek.Infrastructure.Persistence.Migrations;

/// <summary>
/// Implements transactional bidirectional SQLite schema migrations with SHA-256 checksum verification.
/// </summary>
public partial class SqliteDatabaseMigrator : IDatabaseMigrator
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly Assembly _scriptAssembly;

    public SqliteDatabaseMigrator(ISqliteConnectionFactory connectionFactory, Assembly? scriptAssembly = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _scriptAssembly = scriptAssembly ?? typeof(SqliteDatabaseMigrator).Assembly;
    }

    /// <inheritdoc />
    public async Task<MigrationResult> MigrateToLatestAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        int initialVersion = await GetCurrentVersionInternalAsync(connection, cancellationToken).ConfigureAwait(false);
        var availableMigrations = DiscoverMigrationScripts("up").OrderBy(m => m.Version).ToList();

        if (availableMigrations.Count == 0)
        {
            return new MigrationResult(true, initialVersion, initialVersion, 0, "No migration scripts found.");
        }

        // Verify checksums of any already applied migrations
        await VerifyExistingChecksumsAsync(connection, availableMigrations, cancellationToken).ConfigureAwait(false);

        var pending = availableMigrations.Where(m => m.Version > initialVersion).ToList();
        if (pending.Count == 0)
        {
            return new MigrationResult(true, initialVersion, initialVersion, 0, "Database is already at the latest schema version.");
        }

        int appliedCount = 0;
        int currentVersion = initialVersion;

        foreach (var migration in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                Log.Information("Applying SQLite migration {Version}: '{Description}'...", migration.Version, migration.Description);

                await ExecuteSqlScriptAsync(connection, transaction, migration.ScriptContent, cancellationToken).ConfigureAwait(false);

                // Ensure SchemaMigrations table exists and record application
                const string recordSql = @"
                    INSERT INTO SchemaMigrations (Version, AppliedAtUtc, Description, Checksum, ExecutionDurationMs)
                    VALUES (@Version, @AppliedAtUtc, @Description, @Checksum, @ExecutionDurationMs);
                ";

                await using var recordCommand = connection.CreateCommand();
                recordCommand.Transaction = transaction;
                recordCommand.CommandText = recordSql;
                recordCommand.Parameters.AddWithValue("@Version", migration.Version);
                recordCommand.Parameters.AddWithValue("@AppliedAtUtc", DateTime.UtcNow.ToString("o"));
                recordCommand.Parameters.AddWithValue("@Description", migration.Description);
                recordCommand.Parameters.AddWithValue("@Checksum", migration.Checksum);
                recordCommand.Parameters.AddWithValue("@ExecutionDurationMs", stopwatch.ElapsedMilliseconds);

                await recordCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                stopwatch.Stop();
                currentVersion = migration.Version;
                appliedCount++;

                Log.Information("Successfully applied migration {Version} in {Elapsed}ms.", migration.Version, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                Log.Error(ex, "Failed to apply SQLite migration {Version}: {Message}", migration.Version, ex.Message);
                throw new DatabaseMigrationException($"Failed applying migration version {migration.Version} ('{migration.Description}').", migration.Version, _connectionFactory.DatabasePath, ex);
            }
        }

        return new MigrationResult(true, initialVersion, currentVersion, appliedCount, $"Successfully applied {appliedCount} migration(s).");
    }

    /// <inheritdoc />
    public async Task<MigrationResult> RollbackAsync(int targetVersion, CancellationToken cancellationToken = default)
    {
        if (targetVersion < 0)
            throw new ArgumentOutOfRangeException(nameof(targetVersion), targetVersion, "Target rollback version cannot be negative.");

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        int initialVersion = await GetCurrentVersionInternalAsync(connection, cancellationToken).ConfigureAwait(false);
        if (initialVersion <= targetVersion)
        {
            return new MigrationResult(true, initialVersion, initialVersion, 0, $"Current version {initialVersion} is already <= target version {targetVersion}.");
        }

        var downScripts = DiscoverMigrationScripts("down")
            .Where(m => m.Version <= initialVersion && m.Version > targetVersion)
            .OrderByDescending(m => m.Version)
            .ToList();

        int revertedCount = 0;
        int currentVersion = initialVersion;

        foreach (var migration in downScripts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                Log.Information("Reverting SQLite migration {Version}: '{Description}'...", migration.Version, migration.Description);

                await ExecuteSqlScriptAsync(connection, transaction, migration.ScriptContent, cancellationToken).ConfigureAwait(false);

                if (await TableExistsAsync(connection, transaction, "SchemaMigrations", cancellationToken).ConfigureAwait(false))
                {
                    await using var deleteCmd = connection.CreateCommand();
                    deleteCmd.Transaction = transaction;
                    deleteCmd.CommandText = "DELETE FROM SchemaMigrations WHERE Version = @Version;";
                    deleteCmd.Parameters.AddWithValue("@Version", migration.Version);
                    await deleteCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                currentVersion = migration.Version - 1;
                revertedCount++;

                Log.Information("Successfully reverted migration {Version}.", migration.Version);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                Log.Error(ex, "Failed to rollback SQLite migration {Version}: {Message}", migration.Version, ex.Message);
                throw new DatabaseMigrationException($"Failed rolling back migration version {migration.Version}.", migration.Version, _connectionFactory.DatabasePath, ex);
            }
        }

        return new MigrationResult(true, initialVersion, currentVersion, revertedCount, $"Successfully reverted {revertedCount} migration(s).");
    }

    /// <inheritdoc />
    public async Task<int> GetCurrentVersionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await GetCurrentVersionInternalAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MigrationHistoryEntry>> GetMigrationHistoryAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        if (!await TableExistsAsync(connection, null, "SchemaMigrations", cancellationToken).ConfigureAwait(false))
        {
            return Array.Empty<MigrationHistoryEntry>();
        }

        const string sql = "SELECT Version, Description, AppliedAtUtc, Checksum, ExecutionDurationMs FROM SchemaMigrations ORDER BY Version ASC;";
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var entries = new List<MigrationHistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new MigrationHistoryEntry(
                Version: reader.GetInt32(0),
                Description: reader.GetString(1),
                AppliedAtUtc: DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
                Checksum: reader.GetString(3),
                ExecutionDurationMs: reader.GetInt64(4)));
        }

        return entries;
    }

    private static async Task<int> GetCurrentVersionInternalAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, null, "SchemaMigrations", cancellationToken).ConfigureAwait(false))
        {
            return 0;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaMigrations;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return Convert.ToInt32(result);
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, SqliteTransaction? transaction, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = @TableName;";
        command.Parameters.AddWithValue("@TableName", tableName);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(result) > 0;
    }

    private static async Task ExecuteSqlScriptAsync(SqliteConnection connection, SqliteTransaction transaction, string scriptContent, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = scriptContent;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task VerifyExistingChecksumsAsync(SqliteConnection connection, List<MigrationScriptInfo> availableMigrations, CancellationToken cancellationToken)
    {
        var history = await GetMigrationHistoryAsync(cancellationToken).ConfigureAwait(false);
        var historyByVersion = history.ToDictionary(h => h.Version);

        foreach (var available in availableMigrations)
        {
            if (historyByVersion.TryGetValue(available.Version, out var recorded))
            {
                if (!string.Equals(recorded.Checksum, available.Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    throw new DatabaseMigrationException(
                        $"Checksum mismatch for migration version {available.Version} ('{available.Description}'). Recorded: {recorded.Checksum}, Actual: {available.Checksum}.",
                        available.Version,
                        _connectionFactory.DatabasePath);
                }
            }
        }
    }

    private List<MigrationScriptInfo> DiscoverMigrationScripts(string direction)
    {
        var resourceNames = _scriptAssembly.GetManifestResourceNames();
        var regex = MigrationScriptRegex();
        var scripts = new List<MigrationScriptInfo>();

        foreach (var name in resourceNames)
        {
            var match = regex.Match(name);
            if (!match.Success) continue;

            string scriptDirection = match.Groups["direction"].Value;
            if (!string.Equals(scriptDirection, direction, StringComparison.OrdinalIgnoreCase))
                continue;

            int version = int.Parse(match.Groups["version"].Value);
            string description = match.Groups["desc"].Value;

            using var stream = _scriptAssembly.GetManifestResourceStream(name);
            if (stream == null) continue;

            using var reader = new StreamReader(stream, Encoding.UTF8);
            string content = reader.ReadToEnd();
            string checksum = ComputeSha256(content);

            scripts.Add(new MigrationScriptInfo(version, description, direction, content, checksum));
        }

        return scripts;
    }

    private static string ComputeSha256(string content)
    {
        // Normalize CRLF to LF so checksums are deterministic across Windows and Linux checkouts
        string normalized = content.Replace("\r\n", "\n").Trim();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(hash);
    }

    [GeneratedRegex(@"(?:^|\.)(?<version>\d+)_(?<desc>[a-zA-Z0-9_]+)\.(?<direction>up|down)\.sql$", RegexOptions.IgnoreCase)]
    private static partial Regex MigrationScriptRegex();

    private record MigrationScriptInfo(int Version, string Description, string Direction, string ScriptContent, string Checksum);
}
