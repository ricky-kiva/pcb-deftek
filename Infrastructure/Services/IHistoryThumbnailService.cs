using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace pcb_deftek.Infrastructure.Services;

/// <summary>
/// Produces frozen thumbnail images for history cards off the UI thread.
/// </summary>
public interface IHistoryThumbnailService
{
    /// <summary>
    /// Decodes a bounded-size, frozen thumbnail for the specified image.
    /// </summary>
    /// <param name="imagePath">Absolute path to the source image.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A frozen thumbnail, or null when the file is missing, corrupt, or unreadable.</returns>
    Task<BitmapSource?> GetThumbnailAsync(string imagePath, CancellationToken cancellationToken = default);
}
