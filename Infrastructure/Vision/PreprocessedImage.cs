using System;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace pcb_deftek.Infrastructure.Vision;

/// <summary>
/// Encapsulates the output of a vision preprocessing pass, including the dense ONNX input tensor,
/// the geometric letterbox transformation metadata, and an optional OpenCV Mat representation.
/// </summary>
public sealed class PreprocessedImage : IDisposable
{
    private bool _isDisposed;

    /// <summary>
    /// Gets the dense float tensor formatted for model input (typically [1, 3, TargetHeight, TargetWidth]).
    /// </summary>
    public DenseTensor<float> Tensor { get; }

    /// <summary>
    /// Gets the geometric transform metadata used to project model detections back to source image coordinates.
    /// </summary>
    public LetterboxTransform Transform { get; }

    /// <summary>
    /// Gets the letterboxed OpenCV Mat if retention was requested; otherwise null.
    /// </summary>
    public Mat? LetterboxedMat { get; }

    public PreprocessedImage(DenseTensor<float> tensor, LetterboxTransform transform, Mat? letterboxedMat = null)
    {
        Tensor = tensor ?? throw new ArgumentNullException(nameof(tensor));
        Transform = transform ?? throw new ArgumentNullException(nameof(transform));
        LetterboxedMat = letterboxedMat;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        LetterboxedMat?.Dispose();
    }
}
