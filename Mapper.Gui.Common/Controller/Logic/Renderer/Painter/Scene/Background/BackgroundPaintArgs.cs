using Mapper.Gui.Model;
#if AVALONIA
using Avalonia;
#else
using System.Windows;
#endif

namespace Mapper.Gui.Logic
{
    public readonly struct BackgroundPaintArgs
    {
        public Rect BackgroundArea { get; init; }
        public Background Background { get; init; }
    }
}
