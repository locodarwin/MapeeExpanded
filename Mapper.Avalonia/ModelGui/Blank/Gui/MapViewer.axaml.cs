using Avalonia.Controls;
using Avalonia.Platform;
using System;
using System.Reflection;

namespace Mapper.Gui
{
    public partial class MapViewer : Window
    {
        public event EventHandler? Shown;

        public bool HasBeenShown { get; private set; }

        public MapViewer()
        {
            InitializeComponent();

            Version? version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version is not null) Title = $"Mapee Expanded v{version.Major}.{version.Minor}.{version.Build}";

            SetSize();
        }

        public void Initialize(MapViewerArgs args) 
        {
            CanvasContainer.Children.Add(args.Canvas);
            VerticalScrollBarContainer.Children.Add(args.VerticalScrollbar);
            HorizontalScrollBarContainer.Children.Add(args.HorizontalScrollbar);
            FooterGrid.Children.Add(args.Footer);

            foreach (Control widget in args.Widgets)
            {
                widget.ZIndex = 100;
                GlobalContainer.Children.Add(widget);
            }
        }

        private void SetSize()
        {
            Screen? screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is null || WindowState != WindowState.Normal) return;

            double screenWidth = screen.WorkingArea.Width / screen.Scaling;
            double screenHeight = screen.WorkingArea.Height / screen.Scaling;

            Width = screenWidth * (1250 / 1920F);
            Height = screenHeight * (800 / 1080F);

            if (Width < 900 || Height < 700)
            {
                Width = screenWidth * 0.8F;
                Height = screenHeight * 0.9F;
            }
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            if (HasBeenShown) return;
            HasBeenShown = true;

            Shown?.Invoke(this, EventArgs.Empty);
        }
    }
}
