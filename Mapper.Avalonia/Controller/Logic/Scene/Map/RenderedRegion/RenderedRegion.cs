using Mapper.Gui.Model;
using Avalonia.Media.Imaging;

namespace Mapper.Gui.Logic
{
    public class RenderedRegion : IRenderedRegion
    {
        public XzPoint Coords { get; }
        public XzRange? AreaInRegion { get; }
        public Bitmap? Image { get; }

        public RenderedRegion(XzPoint coords, XzRange? areaInRegion, Bitmap? image)
        {
            Coords = coords;
            AreaInRegion = areaInRegion;
            Image = image;
        }
    }
}
