using Avalonia.Controls;
using Avalonia.Media;

namespace Mapper.Gui.Model
{
    public interface IToolButton
    {
        IToggleableTool Tool { get; }
        IImage? Icon { get; }
        string? Name { get; }
        string? ToolTip { get; }
        Button? Button { get; }

        void SetButton(Button button);
    }
}
