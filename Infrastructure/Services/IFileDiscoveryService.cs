using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace pcb_deftek.Infrastructure.Services;

/// <summary>
/// Service contract for discovering, validating, and filtering PCB image files from file paths,
/// path collections, and directory hierarchies.
/// </summary>
public interface IFileDiscoveryService
{
    /// <summary>
    /// Gets the set of supported image file extensions (e.g., .jpg, .png) in lowercase with leading period.
    /// </summary>
    IReadOnlySet<string> SupportedExtensions { get; }

    /// <summary>
    /// Evaluates whether the given file path or file name corresponds to a supported image file format.
    /// </summary>
    /// <param name="filePath">The file path or file name to inspect.</param>
    /// <returns><c>true</c> if supported; otherwise <c>false</c>.</returns>
    bool IsSupportedImageFile(string? filePath);

    /// <summary>
    /// Discovers and validates all supported PCB image files from a single file or directory path.
    /// Directories are traversed recursively.
    /// </summary>
    /// <param name="path">The file path or directory path to inspect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of verified, absolute image file paths.</returns>
    Task<IReadOnlyList<string>> DiscoverImageFilesAsync(string? path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers, validates, and deduplicates all supported PCB image files from a collection of paths.
    /// Paths can be individual file paths or directory paths.
    /// </summary>
    /// <param name="paths">The collection of file or directory paths to inspect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of verified, deduplicated, absolute image file paths.</returns>
    Task<IReadOnlyList<string>> DiscoverImageFilesAsync(IEnumerable<string>? paths, CancellationToken cancellationToken = default);
}
