using System;
#if AVALONIA
#else
using System.Windows.Controls;
#endif

namespace Mapper.Gui.Model
{
    public interface IInformationWidget
    {
        CanvasControl MainControl { get; }
        // The dimension on show, for the cursor's matching place across a portal
        WorldEditor.Dimension? Dimension { get; }

        event EventHandler<SceneInformation>? InformationUpdate;
        event EventHandler<XzPoint>? CursorUpdate;
        event EventHandler<CursorColumn>? ColumnUpdate;
    }
}
