using Mapper.Gui.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Mapper.Gui
{
    public partial class StylePanel : UserControl
    {
        public new IStyle Style { get; }

        private bool _isDown = false;
        private readonly IBrush? _backgroundBorder;
        private readonly IBrush _mouseOverBorder;
        private readonly IBrush _mouseDownBorder;

        public StylePanel(IStyle style)
        {
            InitializeComponent();

            Style = style;
            StyleLabel.Text = Style.Name;
            StyleIcon.Source = Style.Icon;

            _backgroundBorder = MainBorder.BorderBrush;
            _mouseOverBorder = new SolidColorBrush(Color.FromRgb(138, 138, 138));
            _mouseDownBorder = new SolidColorBrush(Color.FromRgb(200, 200, 200));

            StyleIcon.PointerEntered += (sender, e) => ImageContainer.Background = Brushes.White;
            StyleIcon.PointerMoved += (sender, e) => ImageContainer.Background = Brushes.White;
            StyleIcon.PointerExited += (sender, e) => ImageContainer.Background = Brushes.Transparent;
        }

        public void Select()
        {
            IconBorder.BorderThickness = new Thickness(1);
        }
        public void Deselect()
        {
            IconBorder.BorderThickness = new Thickness();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            if (!_isDown) MainBorder.BorderBrush = _mouseOverBorder;
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            MainBorder.BorderBrush = _mouseDownBorder;
            _isDown = true;
        }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            MainBorder.BorderBrush = _mouseOverBorder;
        }
        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);

            MainBorder.BorderBrush = _backgroundBorder;
            _isDown = false;
        }
    }
}
