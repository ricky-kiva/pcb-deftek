using System;

namespace pcb_deftek.Common.Exceptions;

/// <summary>
/// Exception thrown when generating, rendering, or writing an audit report or PDF export fails.
/// </summary>
public class ExportException : PcbDeftekException
{
    /// <summary>
    /// Gets the destination file path of the export that failed.
    /// </summary>
    public string? DestinationPath { get; }

    public ExportException(string message, string? destinationPath = null, Exception? innerException = null)
        : base(message, innerException)
    {
        DestinationPath = destinationPath;
    }
}
