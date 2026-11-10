using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace pcb_deftek.Infrastructure.Persistence.DatabaseBackup;

/// <summary>
/// Contract for managing online hot SQLite database backups, retention pruning, and recovery.
/// </summary>
public interface IDatabaseBackupService
{
    /// <summary>
    /// Gets the absolute directory path where automated backup snapshots are stored.
    /// </summary>
    string BackupDirectory { get; }

    /// <summary>
    /// Creates a hot, point-in-time snapshot of the active database using SQLite's online backup engine.
    /// </summary>
    /// <param name="customDestinationPath">Optional destination path override; if null, auto-generates a timestamped file in <see cref="BackupDirectory"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<BackupResult> CreateBackupAsync(string? customDestinationPath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores the active database from a specified backup file.
    /// </summary>
    /// <param name="backupFilePath">The absolute path to the backup file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<bool> RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a list of all existing backup snapshots in <see cref="BackupDirectory"/> ordered newest first.
    /// </summary>
    Task<IReadOnlyList<BackupFileInfo>> GetBackupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Prunes backup snapshot files older than the specified retention duration.
    /// </summary>
    /// <param name="retentionPeriod">The maximum age of backups to keep (e.g. 30 days).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<int> PruneBackupsAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default);
}

/// <summary>
/// Output record detailing the result of a backup creation operation.
/// </summary>
public record BackupResult(
    bool Success,
    string BackupFilePath,
    long FileSizeBytes,
    DateTime CreatedAtUtc,
    string Message);

/// <summary>
/// Information record describing an existing database backup snapshot.
/// </summary>
public record BackupFileInfo(
    string FilePath,
    string FileName,
    long FileSizeBytes,
    DateTime CreatedAtUtc);
