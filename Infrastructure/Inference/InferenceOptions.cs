namespace pcb_deftek.Infrastructure.Inference;

/// <summary>
/// Configuration options for initializing and running the ONNX inference model session.
/// </summary>
public record InferenceOptions
{
    /// <summary>
    /// Preferred execution device (default is <see cref="ExecutionDevice.GpuDirectML"/> with automatic CPU fallback).
    /// </summary>
    public ExecutionDevice PreferredDevice { get; init; } = ExecutionDevice.GpuDirectML;

    /// <summary>
    /// Target GPU adapter device ID for DirectML (default is 0 for primary GPU).
    /// </summary>
    public int DeviceId { get; init; } = 0;

    /// <summary>
    /// Number of intra-op thread count for CPU execution (0 indicates runtime default).
    /// </summary>
    public int IntraOpNumThreads { get; init; } = 0;

    /// <summary>
    /// Confidence score threshold for keeping candidate defect detections (default 0.65).
    /// </summary>
    public float ConfidenceThreshold { get; init; } = 0.65f;

    /// <summary>
    /// Intersection over Union (IoU) threshold for Non-Maximum Suppression (default 0.45).
    /// </summary>
    public float NmsIouThreshold { get; init; } = 0.45f;

    /// <summary>
    /// Optional path to the ONNX model file; if null, defaults to Assets/Models/best.onnx.
    /// </summary>
    public string? ModelPath { get; init; }
}
