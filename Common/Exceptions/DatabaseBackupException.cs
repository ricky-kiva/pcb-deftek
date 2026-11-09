using System;

namespace pcb_deftek.Common.Exceptions;

/// <summary>
/// Exception thrown when creating or restoring a database backup fails.
/// </summary>
public class DatabaseBackupException : PersistenceException
{
    /// <summary>
    /// Gets the path of the backup file that failed to be created or restored.
    /// </summary>
    public string? BackupPath { get; }

    public DatabaseBackupException(string message, string? backupPath = null, string? databasePath = null, Exception? innerException = null)
        : base(message, databasePath, innerException)
    {
        BackupPath = backupPath;
    }
}
