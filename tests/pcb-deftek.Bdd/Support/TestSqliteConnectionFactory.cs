using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using pcb_deftek.Infrastructure.Persistence;

namespace pcb_deftek.Bdd.Support;

/// <summary>
/// Isolated SQLite connection factory used by BDD scenarios to point persistence
/// services at a temporary database file instead of the user's local application data.
/// </summary>
internal sealed class TestSqliteConnectionFactory : ISqliteConnectionFactory
{
    public string DatabasePath { get; }

    public TestSqliteConnectionFactory(string path) => DatabasePath = path;

    public SqliteConnection CreateConnection() => new($"Data Source={DatabasePath}");

    public async ValueTask<SqliteConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
