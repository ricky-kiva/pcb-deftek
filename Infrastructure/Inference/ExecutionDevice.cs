namespace pcb_deftek.Infrastructure.Inference;

/// <summary>
/// Defines the target hardware execution device for the ONNX inference engine.
/// </summary>
public enum ExecutionDevice
{
    /// <summary>
    /// Standard CPU execution provider.
    /// </summary>
    Cpu = 0,

    /// <summary>
    /// DirectX 12 hardware GPU acceleration using Microsoft DirectML.
    /// </summary>
    GpuDirectML = 1
}
