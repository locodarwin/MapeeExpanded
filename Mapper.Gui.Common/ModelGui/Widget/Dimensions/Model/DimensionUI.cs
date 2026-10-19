using WorldEditor;
#if AVALONIA
using ImageSource = Avalonia.Media.IImage;
#else
using System.Windows.Media;
#endif

namespace Mapper.Gui.Model
{
    public class DimensionUI
    {
        public Dimension Dimension { get; set; }
        public ImageSource Icon { get; set; }

        public DimensionUI(Dimension dimension, ImageSource icon) 
        {
            Dimension = dimension;
            Icon = icon;
        }
    }
}
