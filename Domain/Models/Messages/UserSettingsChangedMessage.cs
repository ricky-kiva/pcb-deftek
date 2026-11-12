using CommunityToolkit.Mvvm.Messaging.Messages;

namespace pcb_deftek.Domain.Models.Messages;

/// <summary>
/// Event message broadcast whenever user settings are updated and saved.
/// </summary>
public class UserSettingsChangedMessage : ValueChangedMessage<UserSettings>
{
    public UserSettingsChangedMessage(UserSettings value) : base(value)
    {
    }
}
