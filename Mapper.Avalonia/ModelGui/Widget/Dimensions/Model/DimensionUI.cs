using Avalonia.Media;
using WorldEditor;

namespace Mapper.Gui.Model
{
    public class DimensionUI
    {
        public Dimension Dimension { get; set; }
        public IImage Icon { get; set; }

        public DimensionUI(Dimension dimension, IImage icon) 
        {
            Dimension = dimension;
            Icon = icon;
        }
    }
}
