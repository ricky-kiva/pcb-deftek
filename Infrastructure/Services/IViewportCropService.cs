using System.Threading;
using System.Threading.Tasks;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Infrastructure.Services;

/// <summary>
/// Result of exporting a normalized source-image region to a PNG.
/// </summary>
/// <param name="FilePath">Full path of the written PNG.</param>
/// <param name="SourceRegion">Normalized region actually cropped after clamping.</param>
/// <param name="PixelWidth">Width of the written image in pixels.</param>
/// <param name="PixelHeight">Height of the written image in pixels.</param>
public sealed record ViewportCropResult(
    string FilePath,
    NormalizedRect SourceRegion,
    int PixelWidth,
    int PixelHeight);

/// <summary>
/// Exports the visible portion of an inspection image as a PNG at native source resolution.
/// </summary>
public interface IViewportCropService
{
    /// <summary>
    /// Crops the specified normalized region from the source image and writes it as a PNG.
    /// The output name is derived from the inspection name; collisions receive a numeric suffix.
    /// </summary>
    /// <param name="sourceImagePath">Absolute path to the source image.</param>
    /// <param name="recordName">Inspection name used to build the output file name.</param>
    /// <param name="visibleRegion">Normalized region to crop; clamped to the image bounds.</param>
    /// <param name="destinationDirectory">
    /// Optional destination directory. Defaults to the configured crop directory.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The written file and the region that was actually exported.</returns>
    Task<ViewportCropResult> CropAsync(
        string sourceImagePath,
        string recordName,
        NormalizedRect visibleRegion,
        string? destinationDirectory = null,
        CancellationToken cancellationToken = default);
}
