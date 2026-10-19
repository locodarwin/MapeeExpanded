#if AVALONIA
using Avalonia.Media;
#else
using System.Windows.Media;
#endif

namespace Mapper.Gui
{
    public struct MouseColorProperties
    {
        public SolidColorBrush Default { get; set; }
        public SolidColorBrush MouseOver { get; set; }
        public SolidColorBrush MouseDown { get; set; }
    }
}
