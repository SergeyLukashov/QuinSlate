using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Raises the UIA live-region event for an element whose text changed. A live region is only
/// announced when something raises the event — nothing does so implicitly for an element whose
/// child text changed.
/// </summary>
internal static class LiveRegionAnnouncer
{
    /// <summary>Asks screen readers to announce <paramref name="element"/>'s current text.</summary>
    public static void Announce(FrameworkElement element)
    {
        AutomationPeer peer = FrameworkElementAutomationPeer.FromElement(element);
        if (peer == null)
        {
            peer = FrameworkElementAutomationPeer.CreatePeerForElement(element);
        }

        if (peer != null)
        {
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }
}
