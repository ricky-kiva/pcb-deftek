using CommunityToolkit.Mvvm.Messaging.Messages;

namespace pcb_deftek.Domain.Models.Messages;

/// <summary>
/// Event message broadcast whenever the user's preferred execution device is modified.
/// </summary>
public class DevicePreferenceChangedMessage : ValueChangedMessage<string>
{
    public DevicePreferenceChangedMessage(string value) : base(value)
    {
    }
}
