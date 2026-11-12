using System;

namespace pcb_deftek.Domain.Models;

/// <summary>
/// Represents metadata for a connected video capture camera device.
/// </summary>
/// <param name="Index">The zero-based hardware device index used by OpenCV VideoCapture.</param>
/// <param name="Name">The human-readable display name of the camera (e.g., "Integrated Camera").</param>
/// <param name="DeviceId">Optional hardware identifier or DirectShow device path.</param>
public sealed record CameraDeviceInfo(
    int Index,
    string Name,
    string? DeviceId = null)
{
    /// <inheritdoc />
    public override string ToString() => Name;
}
