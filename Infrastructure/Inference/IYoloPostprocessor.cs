using System;
using System.Collections.Generic;
using Microsoft.ML.OnnxRuntime.Tensors;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Vision;

namespace pcb_deftek.Infrastructure.Inference;

/// <summary>
/// Contract for decoding raw YOLOv8 output tensors, executing Non-Maximum Suppression (NMS),
/// inverting letterbox coordinates, and constructing verified domain defect entities.
/// </summary>
public interface IYoloPostprocessor
{
    /// <summary>
    /// Parses raw model output tensor, applies confidence filtering and NMS, projects coordinates back
    /// to original image space, and constructs domain <see cref="DetectedDefect"/> entities.
    /// </summary>
    /// <param name="outputTensor">The raw output tensor from model inference (typically [1, 13, 8400]).</param>
    /// <param name="transform">The letterbox transformation parameters used during preprocessing.</param>
    /// <param name="inspectionId">The inspection ID associated with the detected defects.</param>
    /// <param name="confidenceThreshold">Optional confidence threshold override.</param>
    /// <param name="nmsIouThreshold">Optional NMS IoU threshold override.</param>
    /// <returns>A read-only collection of verified domain defect entities.</returns>
    IReadOnlyList<DetectedDefect> Postprocess(
        Tensor<float> outputTensor,
        LetterboxTransform transform,
        Guid inspectionId,
        float? confidenceThreshold = null,
        float? nmsIouThreshold = null);

    /// <summary>
    /// Parses raw model output tensor into intermediate <see cref="YoloDetection"/> records without requiring an inspection ID.
    /// </summary>
    /// <param name="outputTensor">The raw output tensor from model inference.</param>
    /// <param name="transform">The letterbox transformation parameters used during preprocessing.</param>
    /// <param name="confidenceThreshold">Optional confidence threshold override.</param>
    /// <param name="nmsIouThreshold">Optional NMS IoU threshold override.</param>
    /// <returns>A read-only collection of intermediate detections.</returns>
    IReadOnlyList<YoloDetection> ExtractDetections(
        Tensor<float> outputTensor,
        LetterboxTransform transform,
        float? confidenceThreshold = null,
        float? nmsIouThreshold = null);
}
