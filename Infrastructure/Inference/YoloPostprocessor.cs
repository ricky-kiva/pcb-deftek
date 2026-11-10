using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime.Tensors;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Domain.Models;
using pcb_deftek.Infrastructure.Vision;
using Serilog;

namespace pcb_deftek.Infrastructure.Inference;

/// <summary>
/// High-performance YOLOv8 postprocessor that extracts anchor candidate boxes, performs confidence filtering,
/// executes greedy Non-Maximum Suppression (NMS), inverts letterbox transformations, and constructs domain entities.
/// </summary>
public class YoloPostprocessor : IYoloPostprocessor
{
    private const int ExpectedClassCount = 9;
    private const int AttributeCount = 4 + ExpectedClassCount; // cx, cy, w, h + 9 classes = 13

    private readonly float _defaultConfidenceThreshold;
    private readonly float _defaultNmsIouThreshold;

    public YoloPostprocessor(InferenceOptions? options = null)
    {
        _defaultConfidenceThreshold = options?.ConfidenceThreshold ?? 0.25f;
        _defaultNmsIouThreshold = options?.NmsIouThreshold ?? 0.45f;
    }

    /// <inheritdoc />
    public IReadOnlyList<DetectedDefect> Postprocess(
        Tensor<float> outputTensor,
        LetterboxTransform transform,
        Guid inspectionId,
        float? confidenceThreshold = null,
        float? nmsIouThreshold = null)
    {
        ArgumentNullException.ThrowIfNull(outputTensor);
        ArgumentNullException.ThrowIfNull(transform);

        if (inspectionId == Guid.Empty)
        {
            throw new ValidationException("Inspection ID cannot be empty for defect postprocessing.", nameof(inspectionId), inspectionId);
        }

        var rawDetections = ExtractDetections(outputTensor, transform, confidenceThreshold, nmsIouThreshold);

        var defects = new List<DetectedDefect>(rawDetections.Count);
        foreach (var det in rawDetections)
        {
            defects.Add(new DetectedDefect(
                id: Guid.NewGuid(),
                inspectionId: inspectionId,
                classCode: det.ClassCode,
                confidence: det.Confidence,
                boundingBox: det.BoundingBox));
        }

        return defects;
    }

    /// <inheritdoc />
    public IReadOnlyList<YoloDetection> ExtractDetections(
        Tensor<float> outputTensor,
        LetterboxTransform transform,
        float? confidenceThreshold = null,
        float? nmsIouThreshold = null)
    {
        ArgumentNullException.ThrowIfNull(outputTensor);
        ArgumentNullException.ThrowIfNull(transform);

        float confThresh = confidenceThreshold ?? _defaultConfidenceThreshold;
        float iouThresh = nmsIouThreshold ?? _defaultNmsIouThreshold;

        try
        {
            // Parse tensor structure and determine orientation
            var accessor = CreateTensorAccessor(outputTensor);

            // Step 1: Candidate Extraction & Confidence Filtering
            var candidates = new List<CandidateBox>();
            for (int i = 0; i < accessor.CandidateCount; i++)
            {
                // Find highest scoring defect class among the 9 classes
                int bestClassIndex = -1;
                float bestScore = 0f;

                for (int c = 0; c < ExpectedClassCount; c++)
                {
                    float score = accessor.GetValue(4 + c, i);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestClassIndex = c;
                    }
                }

                if (bestScore < confThresh || bestClassIndex < 0)
                {
                    continue;
                }

                float cx = accessor.GetValue(0, i);
                float cy = accessor.GetValue(1, i);
                float w = accessor.GetValue(2, i);
                float h = accessor.GetValue(3, i);

                if (w <= 0f || h <= 0f)
                {
                    continue;
                }

                float x = cx - (w / 2f);
                float y = cy - (h / 2f);

                candidates.Add(new CandidateBox(x, y, w, h, bestClassIndex, bestScore));
            }

            if (candidates.Count == 0)
            {
                return Array.Empty<YoloDetection>();
            }

            // Step 2: Class-Specific Greedy Non-Maximum Suppression (NMS)
            var suppressed = new List<CandidateBox>();
            var classGroups = candidates.GroupBy(c => c.ClassIndex);

            foreach (var group in classGroups)
            {
                var sorted = group.OrderByDescending(c => c.Score).ToList();

                while (sorted.Count > 0)
                {
                    var top = sorted[0];
                    suppressed.Add(top);
                    sorted.RemoveAt(0);

                    sorted.RemoveAll(other => CalculateIoU(top, other) >= iouThresh);
                }
            }

            // Step 3: Coordinate Inversion & DTO Construction
            var results = new List<YoloDetection>(suppressed.Count);
            foreach (var box in suppressed)
            {
                var (normX, normY, normW, normH) = transform.InvertBoxToNormalized(box.X, box.Y, box.Width, box.Height);
                var boundingBox = BoundingBox.FromNormalized(normX, normY, normW, normH);
                var classInfo = DefectKnowledgeBase.GetByIndex(box.ClassIndex);

                results.Add(new YoloDetection(
                    ClassIndex: box.ClassIndex,
                    Class: classInfo.Class,
                    ClassCode: classInfo.Code,
                    ClassName: classInfo.DisplayName,
                    Confidence: box.Score,
                    BoundingBox: boundingBox));
            }

            return results;
        }
        catch (Exception ex) when (ex is not ModelExecutionException && ex is not ValidationException)
        {
            Log.Error(ex, "YOLO postprocessing failed on output tensor: {Message}", ex.Message);
            throw new ModelExecutionException($"YOLO postprocessing failed: {ex.Message}", "Postprocessing", ex);
        }
    }

    private static float CalculateIoU(in CandidateBox a, in CandidateBox b)
    {
        float x1 = Math.Max(a.X, b.X);
        float y1 = Math.Max(a.Y, b.Y);
        float x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        float y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

        float intersectionWidth = Math.Max(0f, x2 - x1);
        float intersectionHeight = Math.Max(0f, y2 - y1);
        float intersectionArea = intersectionWidth * intersectionHeight;

        if (intersectionArea <= 0f)
        {
            return 0f;
        }

        float areaA = a.Width * a.Height;
        float areaB = b.Width * b.Height;
        float unionArea = areaA + areaB - intersectionArea;

        return unionArea > 0f ? (intersectionArea / unionArea) : 0f;
    }

    private static TensorAccessor CreateTensorAccessor(Tensor<float> tensor)
    {
        var dims = tensor.Dimensions;

        // Standard YOLOv8 output: [1, 13, 8400]
        if (dims.Length == 3 && dims[0] == 1 && dims[1] == AttributeCount)
        {
            return new TensorAccessor(dims[2], (attr, cand) => tensor[0, attr, cand]);
        }

        // Transposed YOLOv8 output: [1, 8400, 13]
        if (dims.Length == 3 && dims[0] == 1 && dims[2] == AttributeCount)
        {
            return new TensorAccessor(dims[1], (attr, cand) => tensor[0, cand, attr]);
        }

        // 2D output: [13, 8400]
        if (dims.Length == 2 && dims[0] == AttributeCount)
        {
            return new TensorAccessor(dims[1], (attr, cand) => tensor[attr, cand]);
        }

        // 2D transposed output: [8400, 13]
        if (dims.Length == 2 && dims[1] == AttributeCount)
        {
            return new TensorAccessor(dims[0], (attr, cand) => tensor[cand, attr]);
        }

        throw new ModelExecutionException(
            $"Unsupported YOLOv8 tensor dimensions: [{string.Join(", ", dims.ToArray())}]. Expected 13 attributes and N candidates.",
            "Postprocessing");
    }

    private readonly record struct CandidateBox(
        float X,
        float Y,
        float Width,
        float Height,
        int ClassIndex,
        float Score);

    private sealed class TensorAccessor
    {
        public int CandidateCount { get; }
        private readonly Func<int, int, float> _getter;

        public TensorAccessor(int candidateCount, Func<int, int, float> getter)
        {
            CandidateCount = candidateCount;
            _getter = getter;
        }

        public float GetValue(int attributeIndex, int candidateIndex) => _getter(attributeIndex, candidateIndex);
    }
}
