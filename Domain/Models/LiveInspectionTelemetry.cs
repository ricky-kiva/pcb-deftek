using System;

namespace pcb_deftek.Domain.Models;

/// <summary>
/// Immutable telemetry snapshot representing real-time inference latency, frame rate,
/// and hardware execution metrics for the live camera stream.
/// </summary>
/// <param name="InferenceLatencyMs">Inference latency of the most recent frame in milliseconds.</param>
/// <param name="AverageLatencyMs">Moving average inference latency across recent frames in milliseconds.</param>
/// <param name="Fps">Live frames per second calculated over a rolling 1-second time window.</param>
/// <param name="DeviceName">The active execution device name (e.g., "DirectML GPU" or "CPU").</param>
/// <param name="TimestampUtc">The UTC timestamp when this telemetry snapshot was produced.</param>
public sealed record LiveInspectionTelemetry(
    double InferenceLatencyMs,
    double AverageLatencyMs,
    double Fps,
    string DeviceName,
    DateTime TimestampUtc)
{
    /// <summary>
    /// Gets a default empty telemetry snapshot.
    /// </summary>
    public static LiveInspectionTelemetry Empty => new(
        InferenceLatencyMs: 0.0,
        AverageLatencyMs: 0.0,
        Fps: 0.0,
        DeviceName: "Unknown",
        TimestampUtc: DateTime.UtcNow);
}
