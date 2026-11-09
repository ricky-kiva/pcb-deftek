using System;

namespace pcb_deftek.Common.Exceptions;

/// <summary>
/// Exception thrown when an ONNX model file cannot be located, read, validated, or loaded.
/// </summary>
public class ModelLoadingException : InferenceException
{
    public ModelLoadingException(string message, string? modelPath = null, Exception? innerException = null)
        : base(message, modelPath, innerException)
    {
    }
}
