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

        // The size it had when last closed, kept within the screen. Where it goes is up to the window manager:
        // Wayland doesn't let apps place their windows
        public void ApplySettings(WindowSettings settings)
        {
            Screen? screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (settings.Width is double width && settings.Height is double height && width >= 400 && height >= 300)
            {
                double maxWidth = screen is null ? width : screen.WorkingArea.Width / screen.Scaling;
                double maxHeight = screen is null ? height : screen.WorkingArea.Height / screen.Scaling;
                Width = Math.Min(width, maxWidth);
                Height = Math.Min(height, maxHeight);
            }
            if (settings.Maximized) WindowState = WindowState.Maximized;
        }

        public void SaveSettings(WindowSettings settings)
        {
            settings.Maximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                settings.Width = Bounds.Width;
                settings.Height = Bounds.Height;
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
