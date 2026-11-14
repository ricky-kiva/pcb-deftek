using System.Threading;
using System.Threading.Tasks;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Infrastructure.Services;

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
    /// Optional destination directory. Defaults to the user's Downloads folder.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The full path of the written PNG.</returns>
    Task<string> CropAsync(
        string sourceImagePath,
        string recordName,
        NormalizedRect visibleRegion,
        string? destinationDirectory = null,
        CancellationToken cancellationToken = default);
}
