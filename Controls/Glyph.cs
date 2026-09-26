using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace TaskbarMonitor.Controls;

// Decorative icon: hidden from screen readers, the row title next to it carries the meaning.
public sealed class Glyph : TextBlock
{
    protected override AutomationPeer? OnCreateAutomationPeer() => null;
}
