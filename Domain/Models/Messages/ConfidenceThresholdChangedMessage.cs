using CommunityToolkit.Mvvm.Messaging.Messages;

namespace pcb_deftek.Domain.Models.Messages;

/// <summary>
/// Event message broadcast whenever the active inference confidence threshold is changed.
/// </summary>
public class ConfidenceThresholdChangedMessage : ValueChangedMessage<double>
{
    public ConfidenceThresholdChangedMessage(double value) : base(value)
    {
    }
}
