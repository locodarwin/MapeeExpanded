using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace Mapper.Gui
{
    /// <summary>
    /// Interaction logic for MapViewer.xaml
    /// </summary>
    public partial class MapViewer : Window
    {
        public event EventHandler? Shown;

        public bool HasBeenShown { get; private set; }

        public MapViewer()
        {
            InitializeComponent();
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
                Panel.SetZIndex(widget, 100);
                GlobalContainer.Children.Add(widget);
            }
        }

        private void SetSize()
        {
            Width = SystemParameters.PrimaryScreenWidth * (1250 / 1920F);
            Height = SystemParameters.PrimaryScreenHeight * (800 / 1080F);

            if (Width < 900 || Height < 700)
            {
                Width = SystemParameters.PrimaryScreenWidth * 0.8F;
                Height = SystemParameters.PrimaryScreenHeight * 0.9F;
            }
        }

        // The size and place it had when last closed, as long as that's still on a screen
        public void ApplySettings(WindowSettings settings)
        {
            if (settings.Width is double width && settings.Height is double height && width >= 400 && height >= 300)
            {
                Width = Math.Min(width, SystemParameters.VirtualScreenWidth);
                Height = Math.Min(height, SystemParameters.VirtualScreenHeight);
            }
            if (settings.Left is double left && settings.Top is double top &&
                left >= SystemParameters.VirtualScreenLeft && top >= SystemParameters.VirtualScreenTop &&
                left + 100 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                top + 50 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
            if (settings.Maximized) WindowState = WindowState.Maximized;
        }

        public void SaveSettings(WindowSettings settings)
        {
            // RestoreBounds is the normal size and place, also while maximized
            Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            settings.Maximized = WindowState == WindowState.Maximized;
            if (!bounds.IsEmpty)
            {
                settings.Left = bounds.Left;
                settings.Top = bounds.Top;
                settings.Width = bounds.Width;
                settings.Height = bounds.Height;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            StartupLog.Mark("Main window loaded");
            CustomFrameWindowInitializer frameInitializer = new(this, Template);

            FileVersionInfo fileInfo = FileVersionInfo.GetVersionInfo(Environment.GetCommandLineArgs()[0]);
            Title = fileInfo.ProductName;
            frameInitializer.SetSecondaryTitle($"v{fileInfo.FileVersion}");

            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);

            if (HasBeenShown) return;
            HasBeenShown = true;
            StartupLog.Mark("Main window shown");

            Shown?.Invoke(this, EventArgs.Empty);
        }
    }
}
