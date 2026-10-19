using Avalonia;
using System;
using System.Globalization;
using System.IO;

namespace Mapper.Gui
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            StartupLog.Begin();
            // A world named on the command line is relative to where the user is, so it's resolved before moving
            args = Array.ConvertAll(args, FullPath);

            // Resources are read relative to the working directory, as in the WPF GUI, and an AppImage starts wherever the user is
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            UseGtkScaleOnX11();
            StartupLog.Mark("Starting Avalonia");

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        private static string FullPath(string arg)
        {
            if (arg.StartsWith('-')) return arg;
            if (Uri.TryCreate(arg, UriKind.Absolute, out Uri? uri) && uri.IsFile) return uri.LocalPath;
            return Path.GetFullPath(arg);
        }

        // Native Wayland where there is a compositor, which hands over the monitor's scale (fractional ones too);
        // X11 otherwise, or if Wayland fails to start
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .UseWaylandWithFallback()
                .With(new WaylandPlatformOptions() { AppId = "mapee" })
                .With(new X11PlatformOptions() { WmClass = "mapee" })
                .WithInterFont()
                .LogToTrace();
        }

        // Avalonia's X11 backend scales by its own and Qt's variables or Xft.dpi, but not GTK's GDK_SCALE, which is
        // what some desktops set (Omarchy among them) for X11 apps that get unscaled pixels. Wayland ignores this.
        private static void UseGtkScaleOnX11()
        {
            if (!OperatingSystem.IsLinux()) return;

            foreach (string name in new[] { "AVALONIA_GLOBAL_SCALE_FACTOR", "AVALONIA_SCREEN_SCALE_FACTORS", "QT_SCALE_FACTOR", "QT_SCREEN_SCALE_FACTORS" })
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name))) return;
            }

            if (!double.TryParse(Environment.GetEnvironmentVariable("GDK_SCALE"), NumberStyles.Float, CultureInfo.InvariantCulture, out double scale) || scale <= 1) return;
            if (double.TryParse(Environment.GetEnvironmentVariable("GDK_DPI_SCALE"), NumberStyles.Float, CultureInfo.InvariantCulture, out double dpiScale) && dpiScale > 0) scale *= dpiScale;

            Environment.SetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR", scale.ToString(CultureInfo.InvariantCulture));
        }
    }
}
