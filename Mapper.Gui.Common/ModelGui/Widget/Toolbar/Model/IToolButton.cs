#if AVALONIA
using Avalonia.Controls;
#else
using System.Windows.Controls;
#endif
using System;

namespace Mapper.Gui.Model
{
    public interface IToolButton
    {
        IToggleableTool Tool { get; }
        ToolIcon? Icon { get; }
        // The label under the icon, shown where the window is wide enough
        string? Name { get; }
        string? ToolTip { get; }
        // Opens a menu, so the label has a small arrow
        bool Menu { get; }
        // A count in the corner (Overlays: how many are on); above 0 the button also shows as on
        int Badge { get; }
        event Action? BadgeChanged;
        Button? Button { get; }

        void SetButton(Button button);
    }
}
