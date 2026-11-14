using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Common.Storage;
using Serilog;

namespace pcb_deftek.Infrastructure.Persistence.DatabaseExport;

/// <summary>
/// SQLite implementation of <see cref="IDatabaseExportService"/> utilizing SQLite's online backup engine
/// and WAL checkpointing to create consistent database exports off the UI thread.
/// </summary>
public class SqliteDatabaseExportService : IDatabaseExportService
{
    private const string ExportFilePrefix = "pcb-deftek-export-";
    private readonly ISqliteConnectionFactory _connectionFactory;

    /// <inheritdoc />
    public string DefaultExportDirectory { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="SqliteDatabaseExportService"/>.
    /// </summary>
    /// <param name="connectionFactory">The SQLite connection factory providing access to the active database.</param>
    /// <param name="defaultExportDirectory">Optional override for the default export directory; if omitted, defaults to <c>Downloads\PCB Deftek\Backup</c>.</param>
    public SqliteDatabaseExportService(ISqliteConnectionFactory connectionFactory, string? defaultExportDirectory = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

        DefaultExportDirectory = string.IsNullOrWhiteSpace(defaultExportDirectory)
            ? KnownFolderPaths.GetDatabaseBackupDirectory()
            : Path.GetFullPath(defaultExportDirectory);
    }

    /// <inheritdoc />
    public async Task<string> ExportDatabaseAsync(string? destinationDirectory = null, CancellationToken cancellationToken = default)
    {
        string targetDirectory = string.IsNullOrWhiteSpace(destinationDirectory)
            ? DefaultExportDirectory
            : Path.GetFullPath(destinationDirectory);

        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        string exportFileName = $"{ExportFilePrefix}{timestamp}.db";
        string targetFilePath = Path.Combine(targetDirectory, exportFileName);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            Directory.CreateDirectory(targetDirectory);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "Failed to create destination directory for database export: '{TargetDirectory}'", targetDirectory);
            throw new ExportException($"Failed to create export directory '{targetDirectory}': {ex.Message}", targetFilePath, ex);
        }

        try
        {
            Log.Information("Starting SQLite database export to '{TargetFilePath}'...", targetFilePath);

            // Execute checkpoint and online backup asynchronously off the calling thread
            await Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                await using var sourceConnection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

                // Run a passive checkpoint to commit pending WAL frames prior to backup
                try
                {
                    await using var checkpointCmd = sourceConnection.CreateCommand();
                    checkpointCmd.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
                    await checkpointCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Non-critical WAL checkpoint warning prior to database export: {Message}", ex.Message);
                }

                var destBuilder = new SqliteConnectionStringBuilder
                {
                    DataSource = targetFilePath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Pooling = false
                };

                await using var destinationConnection = new SqliteConnection(destBuilder.ConnectionString);
                await destinationConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                // Utilize SQLite Online Backup API for point-in-time consistent snapshot
                sourceConnection.BackupDatabase(destinationConnection);

                Log.Information("SQLite database export completed successfully. Size: {Size} bytes",
                    new FileInfo(targetFilePath).Length);
            }, cancellationToken).ConfigureAwait(false);

            return targetFilePath;
        }
        catch (OperationCanceledException)
        {
            Log.Warning("SQLite database export to '{TargetFilePath}' was canceled.", targetFilePath);
            TryDeleteFile(targetFilePath);
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error exporting SQLite database to '{TargetFilePath}': {Message}", targetFilePath, ex.Message);
            TryDeleteFile(targetFilePath);
            throw new ExportException($"Failed to export database to '{targetFilePath}': {ex.Message}", targetFilePath, ex);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup on aborted exports
        }
    }
}
