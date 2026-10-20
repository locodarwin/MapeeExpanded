using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Mapper.Gui
{
    /// <summary>
    /// Interaction logic for MapViewer.xaml
    /// </summary>
    public partial class MapViewer : Window
    {
        public event EventHandler? Shown;

        public bool HasBeenShown { get; private set; }

        private CustomFrameWindowInitializer? _frame;
        // How the window was before full screen, to put it back afterwards and to remember meanwhile
        private (WindowState State, Rect Bounds)? _beforeFullScreen;
        public bool IsFullScreen => _beforeFullScreen is not null;

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
            // RestoreBounds is the normal size and place, also while maximized; full screen isn't remembered
            Rect bounds = _beforeFullScreen?.Bounds ?? (WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds);
            settings.Maximized = (_beforeFullScreen?.State ?? WindowState) == WindowState.Maximized;
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
            _frame = frameInitializer;

            FileVersionInfo fileInfo = FileVersionInfo.GetVersionInfo(Environment.ProcessPath!);
            Title = fileInfo.ProductName;
            frameInitializer.SetSecondaryTitle($"v{fileInfo.FileVersion}");

            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        // The whole monitor the window is on, without Mapee's title bar. A plain window that covers its monitor
        // exactly also has Windows keep the taskbar out of the way while it's active
        public void ToggleFullScreen()
        {
            if (!LeaveFullScreen()) EnterFullScreen();
        }

        private void EnterFullScreen()
        {
            if (_frame is null || MonitorBounds() is not Rect monitor) return;

            _beforeFullScreen = (WindowState, WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds);
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.NoResize;
            BorderThickness = new Thickness(0);
            _frame.ShowFrame(false);

            Left = monitor.Left;
            Top = monitor.Top;
            Width = monitor.Width;
            Height = monitor.Height;
        }

        // Whether it was in full screen
        public bool LeaveFullScreen()
        {
            if (_beforeFullScreen is not { } before) return false;
            _beforeFullScreen = null;

            _frame?.ShowFrame(true);
            ResizeMode = ResizeMode.CanResize;
            BorderThickness = new Thickness(1);
            Left = before.Bounds.Left;
            Top = before.Bounds.Top;
            Width = before.Bounds.Width;
            Height = before.Bounds.Height;
            WindowState = before.State;
            return true;
        }

        // In device-independent units
        private Rect? MonitorBounds()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };
            if (handle == IntPtr.Zero || !GetMonitorInfo(MonitorFromWindow(handle, MonitorDefaultToNearest), ref info)) return null;
            if (PresentationSource.FromVisual(this)?.CompositionTarget is not CompositionTarget target) return null;

            Matrix toUnits = target.TransformFromDevice;
            Point topLeft = toUnits.Transform(new Point(info.Monitor.Left, info.Monitor.Top));
            Point bottomRight = toUnits.Transform(new Point(info.Monitor.Right, info.Monitor.Bottom));
            return new Rect(topLeft, bottomRight);
        }

        private const uint MonitorDefaultToNearest = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor, Work;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

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
