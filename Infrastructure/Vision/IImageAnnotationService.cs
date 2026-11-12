using System.Collections.Generic;
using System.Windows.Media.Imaging;
using pcb_deftek.Domain.Models;

namespace pcb_deftek.Infrastructure.Vision;

/// <summary>
/// Service contract for rendering defect bounding boxes and diagnostic labels onto PCB images.
/// </summary>
public interface IImageAnnotationService
{
    /// <summary>
    /// Renders bounding boxes and defect labels on a PCB image from disk, returning a thread-safe frozen <see cref="BitmapSource"/>.
    /// </summary>
    /// <param name="imagePath">The local image file path.</param>
    /// <param name="defects">The collection of detected defects to annotate.</param>
    /// <param name="maxDimension">Optional maximum pixel dimension (width or height) to resize for thumbnail generation.</param>
    /// <returns>A frozen <see cref="BitmapSource"/>, or null if the image could not be loaded.</returns>
    BitmapSource? AnnotateImage(string imagePath, IReadOnlyList<DetectedDefect> defects, int maxDimension = 0);
}
