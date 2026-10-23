using Mapper.Gui.Model;
using System;
#if AVALONIA
using Avalonia.Controls;
#else
using System.Windows.Controls;
#endif

namespace Mapper.Gui.Controller
{
    public class ToolButton : IToolButton
    {
        public IToggleableTool Tool { get; set; }
        public ToolIcon? Icon { get; set; }
        public string? Name { get; set; }
        public string? ToolTip { get; set; }
        public bool Menu { get; set; }
        public Button? Button { get; set; }

        public int Badge
        {
            get => _badge;
            set
            {
                if (_badge == value) return;
                _badge = value;
                BadgeChanged?.Invoke();
            }
        }
        private int _badge;

        public event Action? BadgeChanged;

        public ToolButton(IToggleableTool tool) 
        {
            Tool = tool;
        }

        public void SetButton(Button button)
        {
            Button = button;
        }

        // A new tooltip on the button already made, as when its key is changed
        public void ShowToolTip(string toolTip)
        {
            ToolTip = toolTip;
            if (Button is null) return;
#if AVALONIA
            Avalonia.Controls.ToolTip.SetTip(Button, toolTip);
#else
            Button.ToolTip = toolTip;
#endif
        }
    }
}
