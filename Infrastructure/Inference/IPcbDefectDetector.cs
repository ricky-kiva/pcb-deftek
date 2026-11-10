using System;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace pcb_deftek.Infrastructure.Inference;

/// <summary>
/// High-level contract orchestrating end-to-end PCB defect detection:
/// image preprocessing, model forward pass, postprocessing, NMS, and diagnostic latency tracking.
/// </summary>
public interface IPcbDefectDetector
{
    /// <summary>
    /// Detects defects in an OpenCV Mat image synchronously.
    /// </summary>
    /// <param name="sourceMat">The input image matrix.</param>
    /// <param name="inspectionId">Optional inspection ID; if null, a new Guid is generated.</param>
    /// <param name="confidenceThreshold">Optional confidence threshold override.</param>
    /// <param name="nmsIouThreshold">Optional NMS IoU threshold override.</param>
    InferenceResult Detect(
        Mat sourceMat,
        Guid? inspectionId = null,
        float? confidenceThreshold = null,
        float? nmsIouThreshold = null);

    /// <summary>
    /// Detects defects in an OpenCV Mat image asynchronously.
    /// </summary>
    Task<InferenceResult> DetectAsync(
        Mat sourceMat,
        Guid? inspectionId = null,
        float? confidenceThreshold = null,
        float? nmsIouThreshold = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects defects in an image from a local file path synchronously.
    /// </summary>
    InferenceResult DetectFromFile(
        string filePath,
        Guid? inspectionId = null,
        float? confidenceThreshold = null,
        float? nmsIouThreshold = null);

    /// <summary>
    /// Detects defects in an image from a local file path asynchronously.
    /// </summary>
    Task<InferenceResult> DetectFromFileAsync(
        string filePath,
        Guid? inspectionId = null,
        float? confidenceThreshold = null,
        float? nmsIouThreshold = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects defects in an encoded image byte buffer synchronously.
    /// </summary>
    InferenceResult DetectFromBytes(
        ReadOnlySpan<byte> imageBytes,
        Guid? inspectionId = null,
        float? confidenceThreshold = null,
        float? nmsIouThreshold = null);

    /// <summary>
    /// Detects defects in an encoded image byte array asynchronously.
    /// </summary>
    Task<InferenceResult> DetectFromBytesAsync(
        byte[] imageBytes,
        Guid? inspectionId = null,
        float? confidenceThreshold = null,
        float? nmsIouThreshold = null,
        CancellationToken cancellationToken = default);
}
