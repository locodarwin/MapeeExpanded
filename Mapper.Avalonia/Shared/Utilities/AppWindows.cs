using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.Threading.Tasks;

namespace Mapper.Gui
{
    // How the shared tools open and close their windows; the WPF GUI has the same methods
    public static class AppWindows
    {
        // Runs close when the app is shutting down
        public static void OnExit(Action close)
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.ShutdownRequested += (sender, e) => close();
        }

        // A window that stays open beside the map, kept above the window the user is working in
        public static void ShowTool(Window window)
        {
            if (MessageWindow.ActiveWindow() is Window owner) window.Show(owner);
            else window.Show();
        }

        public static Task ShowDialog(Window window, Window owner)
        {
            return window.ShowDialog(owner);
        }
    }
}
