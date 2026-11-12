using System.Threading;
using System.Threading.Tasks;
using pcb_deftek.Common.Exceptions;

namespace pcb_deftek.Infrastructure.Persistence.DatabaseExport;

/// <summary>
/// Contract for exporting the application database to user-designated destination directories.
/// </summary>
public interface IDatabaseExportService
{
    /// <summary>
    /// Gets the default directory path used when no destination is specified (typically the Windows Downloads folder).
    /// </summary>
    string DefaultExportDirectory { get; }

    /// <summary>
    /// Exports a point-in-time snapshot of the active SQLite database to the specified destination directory.
    /// </summary>
    /// <param name="destinationDirectory">Optional target directory; if null, empty, or whitespace, defaults to <see cref="DefaultExportDirectory"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The absolute path of the generated export database file.</returns>
    /// <exception cref="ExportException">Thrown if database checkpointing, online backup, or filesystem write operations fail.</exception>
    Task<string> ExportDatabaseAsync(string? destinationDirectory = null, CancellationToken cancellationToken = default);
}
