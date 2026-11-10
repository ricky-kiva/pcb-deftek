namespace pcb_deftek.Infrastructure.Inference;

/// <summary>
/// Detailed latency diagnostic breakdown for an inference execution pipeline pass.
/// </summary>
public record InferenceTiming(
    double PreprocessingTimeMs,
    double ModelExecutionTimeMs,
    double PostprocessingTimeMs,
    double TotalTimeMs);
