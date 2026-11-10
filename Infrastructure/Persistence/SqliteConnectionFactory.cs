using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using pcb_deftek.Common.Exceptions;

namespace pcb_deftek.Infrastructure.Persistence;

/// <summary>
/// Provides configured SQLite connections enforcing WAL mode, foreign keys, and safe directory creation.
/// </summary>
public class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private const string DefaultDatabaseFileName = "app.db";
    private readonly string _connectionString;

    public string DatabasePath { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="SqliteConnectionFactory"/>.
    /// </summary>
    /// <param name="databasePath">Optional path override; if omitted or null, defaults to %LOCALAPPDATA%\PcbDeftek\pcb-deftek\app.db.</param>
    public SqliteConnectionFactory(string? databasePath = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appDir = Path.Combine(localAppData, "PcbDeftek", "pcb-deftek");
            Directory.CreateDirectory(appDir);
            DatabasePath = Path.Combine(appDir, DefaultDatabaseFileName);
        }
        else
        {
            DatabasePath = Path.GetFullPath(databasePath);
            string? dir = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            ForeignKeys = true
        };

        _connectionString = builder.ToString();
    }

    /// <inheritdoc />
    public SqliteConnection CreateConnection()
    {
        return new SqliteConnection(_connectionString);
    }

    /// <inheritdoc />
    public async ValueTask<SqliteConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = CreateConnection();

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Enforce pragmas explicitly on each opened connection
            using var command = connection.CreateCommand();
            command.CommandText = @"
                PRAGMA foreign_keys = ON;
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
            ";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            return connection;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw new PersistenceException($"Failed to open SQLite database connection to '{DatabasePath}'.", DatabasePath, ex);
        }
    }
}
