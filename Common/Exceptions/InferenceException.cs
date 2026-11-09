using System;

namespace pcb_deftek.Common.Exceptions;

/// <summary>
/// Exception thrown when a defect inference or model evaluation failure occurs.
/// </summary>
public class InferenceException : PcbDeftekException
{
    /// <summary>
    /// Gets the path to the model involved in the failure, if applicable.
    /// </summary>
    public string? ModelPath { get; }

    public InferenceException(string message)
        : base(message)
    {
    }

    public InferenceException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public InferenceException(string message, string? modelPath, Exception? innerException = null)
        : base(message, innerException)
    {
        ModelPath = modelPath;
    }
}
