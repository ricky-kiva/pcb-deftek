using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using pcb_deftek.Common.Exceptions;
using pcb_deftek.Infrastructure.Vision;
using Serilog;

namespace pcb_deftek.Infrastructure.Inference;

/// <summary>
/// High-performance implementation of <see cref="IPcbDefectDetector"/> orchestrating the end-to-end
/// vision preprocessing, ONNX tensor forward pass, YOLOv8 NMS postprocessing, and diagnostic latency tracking.
/// </summary>
public class PcbDefectDetector : IPcbDefectDetector
{
    private readonly IOnnxModelRunner _modelRunner;
    private readonly IVisionPreprocessor _preprocessor;
    private readonly IYoloPostprocessor _postprocessor;

    public PcbDefectDetector(
        IOnnxModelRunner modelRunner,
        IVisionPreprocessor preprocessor,
        IYoloPostprocessor postprocessor)
    {
        _modelRunner = modelRunner ?? throw new ArgumentNullException(nameof(modelRunner));
        _preprocessor = preprocessor ?? throw new ArgumentNullException(nameof(preprocessor));
        _postprocessor = postprocessor ?? throw new ArgumentNullException(nameof(postprocessor));
    }

    /// <inheritdoc />
    public InferenceResult Detect(
        Mat sourceMat,
        Guid? inspectionId = null,
        float? confidenceThreshold = null,
        float? nmsIouThreshold = null)
    {
        ArgumentNullException.ThrowIfNull(sourceMat);
        if (sourceMat.Empty())
        {
            throw new ImageProcessingException("Input image matrix is empty.", "Validation");
        }

        Guid effectiveId = inspectionId ?? Guid.NewGuid();
        var swTotal = Stopwatch.StartNew();
        var swStage = Stopwatch.StartNew();

        using var preprocessed = _preprocessor.Preprocess(sourceMat);
        double preprocessTimeMs = swStage.Elapsed.TotalMilliseconds;

        return CompletePipeline(preprocessed, effectiveId, confidenceThreshold, nmsIouThreshold, preprocessTimeMs, swTotal);
    }
    /// <inheritdoc />
    public Task<InferenceResult> DetectAsync(
        Mat sourceMat,
        Guid? inspectionId = null,
        float? confidenceThreshold = null,
        float? nmsIouThreshold = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceMat);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Detect(sourceMat, inspectionId, confidenceThreshold, nmsIouThreshold);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public InferenceResult DetectFromFile(
        string filePath,
        Guid? inspectionId = null,
        float? confidenceThreshold = null,
        float? nmsIouThreshold = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileInspectionException($"Image file does not exist: '{filePath}'", filePath, Path.GetExtension(filePath));
        }

        Guid effectiveId = inspectionId ?? Guid.NewGuid();
        var swTotal = Stopwatch.StartNew();
        var swStage = Stopwatch.StartNew();

        using var preprocessed = _preprocessor.PreprocessFromFile(filePath);
        double preprocessTimeMs = swStage.Elapsed.TotalMilliseconds;

        return CompletePipeline(preprocessed, effectiveId, confidenceThreshold, nmsIouThreshold, preprocessTimeMs, swTotal);
    }
    /// <inheritdoc />
    public Task<InferenceResult> DetectFromFileAsync(
        string filePath,
        Guid? inspectionId = null,
        float? confidenceThreshold = null,
        float? nmsIouThreshold = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DetectFromFile(filePath, inspectionId, confidenceThreshold, nmsIouThreshold);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public InferenceResult DetectFromBytes(
        ReadOnlySpan<byte> imageBytes,
        Guid? inspectionId = null,
        float? confidenceThreshold = null,
        float? nmsIouThreshold = null)
    {
        if (imageBytes.IsEmpty)
        {
            throw new ArgumentException("Image byte buffer cannot be empty.", nameof(imageBytes));
        }

        Guid effectiveId = inspectionId ?? Guid.NewGuid();
        var swTotal = Stopwatch.StartNew();
        var swStage = Stopwatch.StartNew();

        using var preprocessed = _preprocessor.PreprocessFromBytes(imageBytes);
        double preprocessTimeMs = swStage.Elapsed.TotalMilliseconds;

        return CompletePipeline(preprocessed, effectiveId, confidenceThreshold, nmsIouThreshold, preprocessTimeMs, swTotal);
    }
    /// <inheritdoc />
    public Task<InferenceResult> DetectFromBytesAsync(
        byte[] imageBytes,
        Guid? inspectionId = null,
        float? confidenceThreshold = null,
        float? nmsIouThreshold = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DetectFromBytes(imageBytes.AsSpan(), inspectionId, confidenceThreshold, nmsIouThreshold);
        }, cancellationToken);
    }

    private InferenceResult CompletePipeline(
        PreprocessedImage preprocessed,
        Guid inspectionId,
        float? confidenceThreshold,
        float? nmsIouThreshold,
        double preprocessTimeMs,
        Stopwatch swTotal)
    {
        var swStage = Stopwatch.StartNew();
        // Stage 2: ONNX Model Forward Pass
        swStage.Restart();
        var inputTensor = NamedOnnxValue.CreateFromTensor(_modelRunner.Metadata.InputName, preprocessed.Tensor);
        using var outputs = _modelRunner.Run(new[] { inputTensor });
        swStage.Stop();
        double inferenceTimeMs = swStage.Elapsed.TotalMilliseconds;

        // Stage 3: YOLOv8 Postprocessing & NMS
        swStage.Restart();
        var firstOutput = outputs.FirstOrDefault();
        if (firstOutput == null)
        {
            throw new ModelExecutionException("ONNX model execution produced no output tensors.", "Postprocessing");
        }

        var outputTensor = firstOutput.AsTensor<float>();
        var defects = _postprocessor.Postprocess(
            outputTensor,
            preprocessed.Transform,
            inspectionId,
            confidenceThreshold,
            nmsIouThreshold);
        swStage.Stop();
        double postprocessTimeMs = swStage.Elapsed.TotalMilliseconds;

        swTotal.Stop();
        double totalTimeMs = swTotal.Elapsed.TotalMilliseconds;

        var timing = new InferenceTiming(
            PreprocessingTimeMs: preprocessTimeMs,
            ModelExecutionTimeMs: inferenceTimeMs,
            PostprocessingTimeMs: postprocessTimeMs,
            TotalTimeMs: totalTimeMs);

        Log.Information(
            "Inference completed for inspection {InspectionId}: {DefectCount} defects detected on {Device} (Total: {Total:F1}ms, Pre: {Pre:F1}ms, Infer: {Infer:F1}ms, Post: {Post:F1}ms)",
            inspectionId,
            defects.Count,
            _modelRunner.ActiveDevice,
            totalTimeMs,
            preprocessTimeMs,
            inferenceTimeMs,
            postprocessTimeMs);

        return new InferenceResult(
            InspectionId: inspectionId,
            Defects: defects,
            Transform: preprocessed.Transform,
            Device: _modelRunner.ActiveDevice,
            Timing: timing);
    }
}
