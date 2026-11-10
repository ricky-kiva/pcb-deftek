using System;
using OpenCvSharp;

namespace pcb_deftek.Infrastructure.Vision;

/// <summary>
/// Contract for computer vision preprocessing, aspect-ratio letterboxing, and planar float tensor conversion for YOLO models.
/// </summary>
public interface IVisionPreprocessor
{
    /// <summary>
    /// Gets the target width in pixels required by the model (default 640).
    /// </summary>
    int TargetWidth { get; }

    /// <summary>
    /// Gets the target height in pixels required by the model (default 640).
    /// </summary>
    int TargetHeight { get; }

    /// <summary>
    /// Preprocesses an OpenCV Mat into a normalized planar float tensor with letterbox padding.
    /// </summary>
    /// <param name="sourceMat">The input BGR image matrix.</param>
    /// <param name="retainLetterboxMat">If true, retains a cloned copy of the letterboxed Mat in the result.</param>
    /// <returns>A preprocessed image payload containing the tensor and letterbox transform.</returns>
    PreprocessedImage Preprocess(Mat sourceMat, bool retainLetterboxMat = false);

    /// <summary>
    /// Decodes an image from a local file path and preprocesses it.
    /// </summary>
    /// <param name="filePath">The absolute or relative file path to the image.</param>
    /// <param name="retainLetterboxMat">If true, retains a cloned copy of the letterboxed Mat in the result.</param>
    /// <returns>A preprocessed image payload containing the tensor and letterbox transform.</returns>
    PreprocessedImage PreprocessFromFile(string filePath, bool retainLetterboxMat = false);

    /// <summary>
    /// Decodes an encoded image from a raw byte buffer and preprocesses it.
    /// </summary>
    /// <param name="imageBytes">The raw bytes of an encoded image (JPEG, PNG, etc.).</param>
    /// <param name="retainLetterboxMat">If true, retains a cloned copy of the letterboxed Mat in the result.</param>
    /// <returns>A preprocessed image payload containing the tensor and letterbox transform.</returns>
    PreprocessedImage PreprocessFromBytes(ReadOnlySpan<byte> imageBytes, bool retainLetterboxMat = false);
}
