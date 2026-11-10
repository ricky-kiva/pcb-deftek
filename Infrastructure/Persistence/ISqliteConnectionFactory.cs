using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace pcb_deftek.Infrastructure.Persistence;

/// <summary>
/// Contract for creating and opening configured SQLite database connections.
/// Enforces WAL mode and foreign key constraints on every connection.
/// </summary>
public interface ISqliteConnectionFactory
{
    /// <summary>
    /// Gets the absolute filesystem path to the managed SQLite database file.
    /// </summary>
    string DatabasePath { get; }

    /// <summary>
    /// Creates a new un-opened <see cref="SqliteConnection"/>.
    /// </summary>
    SqliteConnection CreateConnection();

    /// <summary>
    /// Creates and asynchronously opens a new <see cref="SqliteConnection"/> configured with WAL mode and foreign keys.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask<SqliteConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
}
