using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using pcb_deftek.Common.Exceptions;
using Serilog;

namespace pcb_deftek.Infrastructure.Vision;

/// <summary>
/// High-performance OpenCV implementation of <see cref="IVisionPreprocessor"/> providing SIMD-accelerated
/// aspect-ratio letterbox resizing, border padding, and planar RGB float32 normalization.
/// </summary>
public class OpenCvVisionPreprocessor : IVisionPreprocessor
{
    private static readonly Scalar DefaultPaddingColor = new(114, 114, 114); // Standard neutral gray for YOLO models

    public int TargetWidth { get; }
    public int TargetHeight { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="OpenCvVisionPreprocessor"/> with the specified target dimensions.
    /// </summary>
    /// <param name="targetWidth">Target width in pixels (default 640).</param>
    /// <param name="targetHeight">Target height in pixels (default 640).</param>
    public OpenCvVisionPreprocessor(int targetWidth = 640, int targetHeight = 640)
    {
        if (targetWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetWidth), "Target width must be greater than zero.");
        }
        if (targetHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetHeight), "Target height must be greater than zero.");
        }

        TargetWidth = targetWidth;
        TargetHeight = targetHeight;
    }

    /// <inheritdoc />
    public PreprocessedImage Preprocess(Mat sourceMat, bool retainLetterboxMat = false)
    {
        ArgumentNullException.ThrowIfNull(sourceMat);
        if (sourceMat.Empty())
        {
            throw new ImageProcessingException("Input image matrix is empty.", "Validation");
        }

        int origW = sourceMat.Width;
        int origH = sourceMat.Height;

        Mat? bgrCleanup = null;
        Mat bgrMat = sourceMat;

        try
        {
            // Normalize channel formats to 3-channel BGR
            if (sourceMat.Channels() == 1)
            {
                bgrCleanup = new Mat();
                Cv2.CvtColor(sourceMat, bgrCleanup, ColorConversionCodes.GRAY2BGR);
                bgrMat = bgrCleanup;
            }
            else if (sourceMat.Channels() == 4)
            {
                bgrCleanup = new Mat();
                Cv2.CvtColor(sourceMat, bgrCleanup, ColorConversionCodes.BGRA2BGR);
                bgrMat = bgrCleanup;
            }

            // Calculate uniform aspect-ratio scale factor
            float scale = Math.Min((float)TargetWidth / origW, (float)TargetHeight / origH);
            int newW = Math.Clamp((int)Math.Round(origW * scale), 1, TargetWidth);
            int newH = Math.Clamp((int)Math.Round(origH * scale), 1, TargetHeight);

            // Calculate centering padding offsets
            int padX = (TargetWidth - newW) / 2;
            int padY = (TargetHeight - newH) / 2;
            int padRight = TargetWidth - newW - padX;
            int padBottom = TargetHeight - newH - padY;

            // Resize image preserving aspect ratio
            using var resized = new Mat();
            Cv2.Resize(bgrMat, resized, new Size(newW, newH), interpolation: InterpolationFlags.Linear);

            // Pad borders with neutral gray to reach exact target dimensions
            var letterboxMat = new Mat();
            Cv2.CopyMakeBorder(
                resized,
                letterboxMat,
                top: padY,
                bottom: padBottom,
                left: padX,
                right: padRight,
                borderType: BorderTypes.Constant,
                value: DefaultPaddingColor);

            // Convert to NCHW planar RGB float32 tensor normalized to [0.0, 1.0]
            using var blob = CvDnn.BlobFromImage(
                letterboxMat,
                scaleFactor: 1.0 / 255.0,
                size: new Size(TargetWidth, TargetHeight),
                mean: new Scalar(0, 0, 0),
                swapRB: true, // Convert BGR to RGB
                crop: false);

            int totalElements = 1 * 3 * TargetHeight * TargetWidth;
            var floatBuffer = new float[totalElements];
            Marshal.Copy(blob.Data, floatBuffer, 0, totalElements);

            var tensor = new DenseTensor<float>(floatBuffer, new[] { 1, 3, TargetHeight, TargetWidth });
            var transform = new LetterboxTransform(origW, origH, TargetWidth, TargetHeight, scale, padX, padY);

            Mat? retainedMat = retainLetterboxMat ? letterboxMat : null;
            if (!retainLetterboxMat)
            {
                letterboxMat.Dispose();
            }

            return new PreprocessedImage(tensor, transform, retainedMat);
        }
        catch (Exception ex) when (ex is not ImageProcessingException)
        {
            Log.Error(ex, "Failed to preprocess image matrix ({Width}x{Height}): {Message}", origW, origH, ex.Message);
            throw new ImageProcessingException($"Preprocessing failed on {origW}x{origH} matrix: {ex.Message}", "Preprocess", ex);
        }
        finally
        {
            bgrCleanup?.Dispose();
        }
    }

    /// <inheritdoc />
    public PreprocessedImage PreprocessFromFile(string filePath, bool retainLetterboxMat = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileInspectionException($"Image file does not exist: '{filePath}'", filePath, Path.GetExtension(filePath));
        }

        try
        {
            using var mat = Cv2.ImRead(filePath, ImreadModes.Color);
            if (mat.Empty())
            {
                throw new FileInspectionException($"Failed to decode image from '{filePath}'.", filePath, Path.GetExtension(filePath));
            }

            return Preprocess(mat, retainLetterboxMat);
        }
        catch (Exception ex) when (ex is not InspectionException)
        {
            Log.Error(ex, "Failed reading image from file '{FilePath}': {Message}", filePath, ex.Message);
            throw new FileInspectionException($"Error reading image from '{filePath}': {ex.Message}", filePath, Path.GetExtension(filePath), ex);
        }
    }

    /// <inheritdoc />
    public PreprocessedImage PreprocessFromBytes(ReadOnlySpan<byte> imageBytes, bool retainLetterboxMat = false)
    {
        if (imageBytes.IsEmpty)
        {
            throw new ArgumentException("Image byte buffer cannot be empty.", nameof(imageBytes));
        }

        try
        {
            using var mat = Cv2.ImDecode(imageBytes.ToArray(), ImreadModes.Color);
            if (mat.Empty())
            {
                throw new ImageProcessingException("Failed to decode image from memory byte buffer.", "ImDecode");
            }

            return Preprocess(mat, retainLetterboxMat);
        }
        catch (Exception ex) when (ex is not InspectionException)
        {
            Log.Error(ex, "Failed decoding image bytes: {Message}", ex.Message);
            throw new ImageProcessingException($"Error decoding image bytes: {ex.Message}", "ImDecode", ex);
        }
    }
}
