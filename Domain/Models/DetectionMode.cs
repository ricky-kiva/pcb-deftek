namespace pcb_deftek.Domain.Models;

/// <summary>
/// Specifies the source modality or capture channel of an inspection session.
/// </summary>
public enum DetectionMode
{
    SingleFile = 0,
    BatchFolder = 1,
    LiveCamera = 2
}
