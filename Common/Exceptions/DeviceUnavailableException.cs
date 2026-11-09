using System;

namespace pcb_deftek.Common.Exceptions;

/// <summary>
/// Exception thrown when a requested execution provider (such as DirectML GPU) is unsupported or unavailable.
/// </summary>
public class DeviceUnavailableException : HardwareDeviceException
{
    /// <summary>
    /// Gets the name of the execution provider requested (e.g., DirectML, CUDA).
    /// </summary>
    public string? ProviderName { get; }

    public DeviceUnavailableException(string message, string? providerName = null, Exception? innerException = null)
        : base(message, providerName, innerException)
    {
        ProviderName = providerName;
    }
}
