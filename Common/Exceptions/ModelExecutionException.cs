using System;

namespace pcb_deftek.Common.Exceptions;

/// <summary>
/// Exception thrown when model forward pass, tensor allocation, or post-processing fails during execution.
/// </summary>
public class ModelExecutionException : InferenceException
{
    /// <summary>
    /// Gets the execution pipeline stage where the failure occurred (e.g., Preprocessing, ForwardPass, NMS).
    /// </summary>
    public string? ExecutionStage { get; }

    public ModelExecutionException(string message, string? executionStage = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ExecutionStage = executionStage;
    }
}
