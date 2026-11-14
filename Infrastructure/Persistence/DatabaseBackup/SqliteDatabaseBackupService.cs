using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Common.Storage;
using Serilog;

namespace pcb_deftek.Infrastructure.Persistence.DatabaseBackup;

/// <summary>
/// SQLite implementation of <see cref="IDatabaseBackupService"/> using SQLite's online backup engine.
/// </summary>
public class SqliteDatabaseBackupService : IDatabaseBackupService
{
    private const string BackupFilePrefix = "app-backup-";
    private readonly ISqliteConnectionFactory _connectionFactory;

    public string BackupDirectory { get; }

    public SqliteDatabaseBackupService(ISqliteConnectionFactory connectionFactory, string? backupDirectory = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

        if (string.IsNullOrWhiteSpace(backupDirectory))
        {
            BackupDirectory = KnownFolderPaths.GetDatabaseBackupDirectory();
        }
        else
        {
            BackupDirectory = Path.GetFullPath(backupDirectory);
        }

        Directory.CreateDirectory(BackupDirectory);
    }

    /// <inheritdoc />
    public async Task<BackupResult> CreateBackupAsync(string? customDestinationPath = null, CancellationToken cancellationToken = default)
    {
        string destinationPath = string.IsNullOrWhiteSpace(customDestinationPath)
            ? Path.Combine(BackupDirectory, $"{BackupFilePrefix}{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.db")
            : Path.GetFullPath(customDestinationPath);

        string? destDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        try
        {
            Log.Information("Creating online SQLite backup to '{DestinationPath}'...", destinationPath);

            await using var sourceConnection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            var destBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = destinationPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            };

            await using (var destinationConnection = new SqliteConnection(destBuilder.ToString()))
            {
                await destinationConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

                // Execute online hot backup
                sourceConnection.BackupDatabase(destinationConnection);

                // Run integrity verification on backup
                await using var verifyCmd = destinationConnection.CreateCommand();
                verifyCmd.CommandText = "PRAGMA integrity_check;";
                var integrityResult = Convert.ToString(await verifyCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));

                if (!string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new DatabaseBackupException($"Backup database failed integrity check: {integrityResult}", destinationPath, _connectionFactory.DatabasePath);
                }
            }

            var fileInfo = new FileInfo(destinationPath);
            Log.Information("Successfully created database backup at '{DestinationPath}' ({Bytes} bytes).", destinationPath, fileInfo.Length);

            return new BackupResult(
                Success: true,
                BackupFilePath: destinationPath,
                FileSizeBytes: fileInfo.Length,
                CreatedAtUtc: DateTime.UtcNow,
                Message: "Backup completed and verified successfully.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not DatabaseBackupException)
        {
            Log.Error(ex, "Failed to create database backup at '{DestinationPath}': {Message}", destinationPath, ex.Message);
            throw new DatabaseBackupException($"Failed creating database backup to '{destinationPath}'.", destinationPath, _connectionFactory.DatabasePath, ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFilePath);

        if (!File.Exists(backupFilePath))
        {
            throw new FileNotFoundException($"Backup file '{backupFilePath}' does not exist.", backupFilePath);
        }

        try
        {
            Log.Information("Restoring active database from backup file '{BackupPath}'...", backupFilePath);

            var backupBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = backupFilePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            };

            await using var backupConnection = new SqliteConnection(backupBuilder.ToString());
            await backupConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Verify integrity of backup file before restoring
            await using (var verifyCmd = backupConnection.CreateCommand())
            {
                verifyCmd.CommandText = "PRAGMA integrity_check;";
                var integrityResult = Convert.ToString(await verifyCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));

                if (!string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new DatabaseBackupException($"Cannot restore from corrupt backup: {integrityResult}", backupFilePath, _connectionFactory.DatabasePath);
                }
            }

            // Stream pages from backup connection into target active database
            await using (var targetConnection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false))
            {
                backupConnection.BackupDatabase(targetConnection);
            }

            Log.Information("Successfully restored database from '{BackupPath}'.", backupFilePath);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not DatabaseBackupException)
        {
            Log.Error(ex, "Failed restoring database from backup '{BackupPath}': {Message}", backupFilePath, ex.Message);
            throw new DatabaseBackupException($"Failed restoring database from '{backupFilePath}'.", backupFilePath, _connectionFactory.DatabasePath, ex);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BackupFileInfo>> GetBackupsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(BackupDirectory))
        {
            return Task.FromResult<IReadOnlyList<BackupFileInfo>>(Array.Empty<BackupFileInfo>());
        }

        var files = Directory.GetFiles(BackupDirectory, "*.db")
            .Select(f => new FileInfo(f))
            .OrderByDescending(GetFileTimestamp)
            .Select(f => new BackupFileInfo(f.FullName, f.Name, f.Length, GetFileTimestamp(f)))
            .ToList();
        return Task.FromResult<IReadOnlyList<BackupFileInfo>>(files);
    }

    /// <inheritdoc />
    public Task<int> PruneBackupsAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(BackupDirectory))
        {
            return Task.FromResult(0);
        }

        var cutoff = DateTime.UtcNow - retentionPeriod;
        int deletedCount = 0;

        var files = Directory.GetFiles(BackupDirectory, $"{BackupFilePrefix}*.db")
            .Select(f => new FileInfo(f))
            .Where(f => GetFileTimestamp(f) < cutoff)
            .ToList();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                file.Delete();
                deletedCount++;
                Log.Information("Pruned old backup file '{FileName}' (Created: {CreatedAt}).", file.Name, file.CreationTimeUtc);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to prune old backup file '{FileName}'.", file.Name);
            }
        }

        return Task.FromResult(deletedCount);
    }

    private static DateTime GetFileTimestamp(FileInfo file)
    {
        DateTime fileTime = file.CreationTimeUtc < file.LastWriteTimeUtc ? file.CreationTimeUtc : file.LastWriteTimeUtc;

        string name = Path.GetFileNameWithoutExtension(file.Name);
        if (name.StartsWith(BackupFilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            string rawTimestamp = name.Substring(BackupFilePrefix.Length);
            if (DateTime.TryParseExact(rawTimestamp, "yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt) ||
                DateTime.TryParseExact(rawTimestamp, "yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out dt))
            {
                var parsedUtc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                return parsedUtc < fileTime ? parsedUtc : fileTime;
            }
        }
        return fileTime;
    }
}
