using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace pcb_deftek.Infrastructure.Services;

/// <summary>
/// Default implementation of <see cref="IFileDiscoveryService"/> providing file extension validation,
/// recursive directory traversal, hidden/empty file filtering, and path deduplication.
/// </summary>
public class FileDiscoveryService : IFileDiscoveryService
{
    private static readonly HashSet<string> DefaultExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".bmp",
        ".webp"
    };

    /// <inheritdoc />
    public IReadOnlySet<string> SupportedExtensions => DefaultExtensions;

    /// <inheritdoc />
    public bool IsSupportedImageFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        ReadOnlySpan<char> extension = Path.GetExtension(filePath.AsSpan());
        if (extension.IsEmpty)
        {
            return false;
        }

        return DefaultExtensions.Contains(extension.ToString());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> DiscoverImageFilesAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        return DiscoverImageFilesAsync(new[] { path }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> DiscoverImageFilesAsync(IEnumerable<string>? paths, CancellationToken cancellationToken = default)
    {
        if (paths == null)
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        return Task.Run(() =>
        {
            var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawPath in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(rawPath.Trim());

                if (File.Exists(fullPath))
                {
                    if (IsValidImageFile(fullPath))
                    {
                        discovered.Add(fullPath);
                    }
                }
                else if (Directory.Exists(fullPath))
                {
                    TraverseDirectory(fullPath, discovered, cancellationToken);
                }
            }

            IReadOnlyList<string> result = discovered
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return result;
        }, cancellationToken);
    }

    private void TraverseDirectory(string directoryPath, HashSet<string> targetSet, CancellationToken cancellationToken)
    {
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
        };

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*", enumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsValidImageFile(file))
            {
                targetSet.Add(Path.GetFullPath(file));
            }
        }
    }

    private bool IsValidImageFile(string filePath)
    {
        if (!IsSupportedImageFile(filePath))
        {
            return false;
        }

        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists || info.Length == 0)
            {
                return false;
            }

            if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
            {
                return false;
            }

            return true;
        }
        catch
        {
            // If file cannot be inspected (e.g. access denied), skip it
            return false;
        }
    }
}
