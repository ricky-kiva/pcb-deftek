using CommunityToolkit.Mvvm.Messaging.Messages;

namespace pcb_deftek.Domain.Models.Messages;

/// <summary>
/// Event message broadcast when a view requests the shell navigation sidebar to collapse or expand.
/// When <see cref="ValueChangedMessage{T}.Value"/> is <c>true</c>, the sidebar should collapse;
/// when <c>false</c>, the sidebar should expand.
/// </summary>
public sealed class SidebarCollapseMessage : ValueChangedMessage<bool>
{
    public SidebarCollapseMessage(bool shouldCollapse) : base(shouldCollapse)
    {
    }
}
