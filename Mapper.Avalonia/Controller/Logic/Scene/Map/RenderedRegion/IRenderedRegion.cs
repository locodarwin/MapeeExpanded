using Mapper.Gui.Model;
using Avalonia.Media.Imaging;

namespace Mapper.Gui.Logic
{
    public interface IRenderedRegion
    {
        XzPoint Coords { get; }
        XzRange? AreaInRegion { get; }
        Bitmap? Image { get; }
    }
}
