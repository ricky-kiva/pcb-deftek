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

    /// <summary>
    /// Renders bounding boxes and defect labels directly onto an in-memory OpenCV <see cref="OpenCvSharp.Mat"/> frame,
    /// returning a thread-safe frozen <see cref="BitmapSource"/>.
    /// </summary>
    /// <param name="sourceMat">The raw video frame matrix.</param>
    /// <param name="defects">The collection of detected defects to annotate.</param>
    /// <param name="maxDimension">Optional maximum pixel dimension to resize.</param>
    /// <returns>A frozen <see cref="BitmapSource"/>, or null if the frame cannot be processed.</returns>
    BitmapSource? AnnotateMat(OpenCvSharp.Mat sourceMat, IReadOnlyList<DetectedDefect> defects, int maxDimension = 0);

    /// <summary>
    /// Converts an OpenCV <see cref="OpenCvSharp.Mat"/> directly into a thread-safe frozen WPF <see cref="BitmapSource"/>
    /// without annotation or disk I/O.
    /// </summary>
    /// <param name="sourceMat">The input image matrix.</param>
    /// <returns>A frozen <see cref="BitmapSource"/>, or null if conversion fails.</returns>
    BitmapSource? MatToBitmapSource(OpenCvSharp.Mat sourceMat);
}
