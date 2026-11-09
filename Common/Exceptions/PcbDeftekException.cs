using System;

namespace pcb_deftek.Common.Exceptions;

/// <summary>
/// Abstract base exception for all domain and operational exceptions in pcb-deftek.
/// </summary>
public abstract class PcbDeftekException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PcbDeftekException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    protected PcbDeftekException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PcbDeftekException"/> class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    protected PcbDeftekException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
