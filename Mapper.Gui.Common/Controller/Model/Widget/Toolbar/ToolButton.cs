using Mapper.Gui.Model;
#if AVALONIA
using Avalonia.Controls;
using ImageSource = Avalonia.Media.IImage;
#else
using System.Windows.Controls;
using System.Windows.Media;
#endif

namespace Mapper.Gui.Controller
{
    public class ToolButton : IToolButton
    {
        public IToggleableTool Tool { get; set; }
        public ImageSource? Icon { get; set; }
        public string? Name { get; set; }
        public string? ToolTip { get; set; }
        public Button? Button { get; set; }

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
