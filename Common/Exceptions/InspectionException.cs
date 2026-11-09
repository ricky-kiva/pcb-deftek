using System;

namespace pcb_deftek.Common.Exceptions;

/// <summary>
/// Base exception for errors encountered during media inspection and analysis operations.
/// </summary>
public class InspectionException : PcbDeftekException
{
    /// <summary>
    /// Gets the path or identifier of the inspection target involved in the failure.
    /// </summary>
    public string? TargetPath { get; }

    public InspectionException(string message, string? targetPath = null, Exception? innerException = null)
        : base(message, innerException)
    {
        TargetPath = targetPath;
    }
}
