using System;

namespace pcb_deftek.Common.Exceptions;

/// <summary>
/// Base exception for data storage, SQLite access, and transaction failures.
/// </summary>
public class PersistenceException : PcbDeftekException
{
    /// <summary>
    /// Gets the path to the database file associated with the failure, if applicable.
    /// </summary>
    public string? DatabasePath { get; }

    public PersistenceException(string message, string? databasePath = null, Exception? innerException = null)
        : base(message, innerException)
    {
        DatabasePath = databasePath;
    }
}
