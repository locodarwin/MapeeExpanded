using Mapper.Gui.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Mapper.Gui
{
    public partial class DimensionButtonPanel : UserControl
    {
        public DimensionUI Dimension { get; }

        public DimensionButtonPanel(DimensionUI dimension)
        {
            InitializeComponent();

            Dimension = dimension;
            DimensionIcon.Source = Dimension.Icon;
            ToolTip.SetTip(this, $"Go to {Dimension.Dimension.Name}");

            DimensionIcon.PointerEntered += (sender, e) => ImageContainer.Background = Brushes.White;
            DimensionIcon.PointerMoved += (sender, e) => ImageContainer.Background = Brushes.White;
            DimensionIcon.PointerExited += (sender, e) => ImageContainer.Background = Brushes.Transparent;
        }

        public void Select()
        {
            DimensionBorder.BorderThickness = new Thickness(2);
        }
        public void Deselect()
        {
            DimensionBorder.BorderThickness = new Thickness();
        }
    }
}
