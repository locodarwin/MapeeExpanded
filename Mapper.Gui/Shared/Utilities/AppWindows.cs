using System;
using System.Threading.Tasks;
using System.Windows;

namespace Mapper.Gui
{
    // How the shared tools open and close their windows; the Avalonia GUI has the same methods
    public static class AppWindows
    {
        // Runs close when the app is closing
        public static void OnExit(Action close)
        {
            Application.Current.MainWindow.Closing += (sender, e) => close();
        }

        // A window that stays open beside the map
        public static void ShowTool(Window window)
        {
            window.Show();
        }

        // A modal window; WPF dialogs place themselves, so the owner is only Avalonia's
        public static Task ShowDialog(Window window, Window owner)
        {
            window.ShowDialog();
            return Task.CompletedTask;
        }
    }
}
