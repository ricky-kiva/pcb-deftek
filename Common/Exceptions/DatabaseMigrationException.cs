using System;

namespace pcb_deftek.Common.Exceptions;

/// <summary>
/// Exception thrown when applying schema migrations or version upgrades to the SQLite database fails.
/// </summary>
public class DatabaseMigrationException : PersistenceException
{
    /// <summary>
    /// Gets the schema version that failed to be applied, if applicable.
    /// </summary>
    public int? TargetVersion { get; }

    public DatabaseMigrationException(string message, int? targetVersion = null, string? databasePath = null, Exception? innerException = null)
        : base(message, databasePath, innerException)
    {
        TargetVersion = targetVersion;
    }
}
