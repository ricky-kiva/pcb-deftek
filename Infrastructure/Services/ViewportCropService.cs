using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Common.Storage;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Infrastructure.Services;

/// <summary>
/// OpenCV-backed viewport crop exporter. Crops the requested normalized region from the
/// source image at native resolution and writes a collision-safe PNG.
/// </summary>
public sealed class ViewportCropService : IViewportCropService
{
    private const string CropSuffix = "_crop_";
    private const int MaxNameLength = 80;

    private readonly ILogger<ViewportCropService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ViewportCropService"/> class.
    /// </summary>
    public ViewportCropService(ILogger<ViewportCropService>? logger = null)
    {
        _logger = logger ?? NullLogger<ViewportCropService>.Instance;
    }

    /// <inheritdoc />
    public Task<string> CropAsync(
        string sourceImagePath,
        string recordName,
        NormalizedRect visibleRegion,
        string? destinationDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceImagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordName);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () => Crop(sourceImagePath, recordName, visibleRegion, destinationDirectory, cancellationToken),
            cancellationToken);
    }

    private string Crop(
        string sourceImagePath,
        string recordName,
        NormalizedRect visibleRegion,
        string? destinationDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var region = NormalizedRect.Clamp(
            visibleRegion.X,
            visibleRegion.Y,
            visibleRegion.Width,
            visibleRegion.Height);

        if (region.IsEmpty)
        {
            throw new ImageProcessingException("The visible region is empty.", "Crop");
        }

        using var source = Cv2.ImRead(sourceImagePath, ImreadModes.Unchanged);
        if (source.Empty())
        {
            throw new ImageProcessingException(
                $"Could not read source image '{sourceImagePath}'.",
                "ReadSource");
        }

        int x = Math.Clamp((int)Math.Round(region.X * source.Width), 0, source.Width - 1);
        int y = Math.Clamp((int)Math.Round(region.Y * source.Height), 0, source.Height - 1);
        int width = Math.Clamp((int)Math.Round(region.Width * source.Width), 1, source.Width - x);
        int height = Math.Clamp((int)Math.Round(region.Height * source.Height), 1, source.Height - y);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var cropped = new Mat(source, new Rect(x, y, width, height));
            string outputDirectory = string.IsNullOrWhiteSpace(destinationDirectory)
                ? KnownFolderPaths.GetDownloadsDirectory()
                : Path.GetFullPath(destinationDirectory);

            Directory.CreateDirectory(outputDirectory);

            Cv2.ImEncode(".png", cropped, out byte[] pngBytes);
            if (pngBytes.Length == 0)
            {
                throw new ImageProcessingException("OpenCV produced an empty PNG buffer.", "WritePng");
            }

            string outputPath = WriteUniquePng(outputDirectory, recordName, pngBytes);

            _logger.LogInformation(
                "Exported viewport crop '{OutputPath}' ({Width}x{Height}).",
                outputPath,
                width,
                height);

            return outputPath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ImageProcessingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ImageProcessingException($"Failed to crop the viewport: {ex.Message}", "Crop", ex);
        }
    }

    private static string WriteUniquePng(string directory, string recordName, byte[] pngBytes)
    {
        string baseName = SanitizeName(recordName) + CropSuffix +
            DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        for (int suffix = 1; suffix <= 10_000; suffix++)
        {
            string candidate = Path.Combine(
                directory,
                suffix == 1 ? $"{baseName}.png" : $"{baseName}_{suffix}.png");

            try
            {
                using var stream = new FileStream(
                    candidate,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                stream.Write(pngBytes);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // Another writer created this name; try the next suffix.
            }
        }

        throw new ImageProcessingException("Could not find an unused crop file name.", "WritePng");
    }

    private static string SanitizeName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name
            .Trim()
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray())
            .Trim(' ', '.');

        if (sanitized.Length > MaxNameLength)
        {
            sanitized = sanitized[..MaxNameLength].TrimEnd(' ', '.');
        }

        return string.IsNullOrWhiteSpace(sanitized) ? "inspection" : sanitized;
    }
}
