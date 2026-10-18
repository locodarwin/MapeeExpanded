using Mapper.Gui.Model;
using Avalonia;
using Avalonia.Media.Imaging;

namespace Mapper.Gui.Logic
{
    public interface ICheckerImageGenerator
    {
        Bitmap Generate(PixelSize size, int checkerSize, ColorPair pair);
    }
}
