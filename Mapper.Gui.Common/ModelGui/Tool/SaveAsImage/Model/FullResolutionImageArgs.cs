#if AVALONIA
using Avalonia.Media;
#else
using System.Windows.Media;
#endif

namespace Mapper.Gui.Model
{
    public readonly struct FullResolutionImageArgs
    {
        public bool ClipArea { get; init; }
        public bool CheckerPatternEnabled { get; init; }
        public Color BackgroundColor { get; init; }
    }
}
