using System;

namespace Mapper.Gui.Model
{
    public interface IInformationWidget
    {
        CanvasControl MainControl { get; }

        event EventHandler<SceneInformation>? InformationUpdate;
        event EventHandler<XzPoint>? CursorUpdate;
    }
}
