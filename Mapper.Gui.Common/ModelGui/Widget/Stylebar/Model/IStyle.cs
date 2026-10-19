#if AVALONIA
using ImageSource = Avalonia.Media.IImage;
#else
using System.Windows.Media;
#endif

namespace Mapper.Gui.Model
{
    public interface IStyle
    {
        ImageSource? Icon { get; }
        string Name { get; }
        string Id { get; }
    }
}
