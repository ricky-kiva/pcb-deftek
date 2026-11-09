using System;

namespace pcb_deftek.Common.Exceptions;

/// <summary>
/// Exception thrown when a hardware device, execution provider, or peripheral interaction fails.
/// </summary>
public class HardwareDeviceException : PcbDeftekException
{
    /// <summary>
    /// Gets the name or identifier of the hardware device involved in the failure.
    /// </summary>
    public string? DeviceName { get; }

    public HardwareDeviceException(string message, string? deviceName = null, Exception? innerException = null)
        : base(message, innerException)
    {
        DeviceName = deviceName;
    }
}
