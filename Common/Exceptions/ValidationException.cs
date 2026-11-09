using System;

namespace pcb_deftek.Common.Exceptions;

/// <summary>
/// Exception thrown when user input, confidence bounds, or filter criteria violate validation rules.
/// </summary>
public class ValidationException : PcbDeftekException
{
    /// <summary>
    /// Gets the name of the property or parameter that failed validation.
    /// </summary>
    public string? PropertyName { get; }

    /// <summary>
    /// Gets the invalid value that was attempted.
    /// </summary>
    public object? AttemptedValue { get; }

    public ValidationException(string message, string? propertyName = null, object? attemptedValue = null, Exception? innerException = null)
        : base(message, innerException)
    {
        PropertyName = propertyName;
        AttemptedValue = attemptedValue;
    }
}
