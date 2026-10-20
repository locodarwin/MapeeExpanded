using Mapper.Gui.Controller;
using System.Collections.Generic;
using System.Windows;

namespace Mapper.Gui
{
    // Feeds files dragged onto the main window to WorldDrop. Moving from one control to another inside the window
    // raises a leave and an enter on the way, so a leave only counts once the pointer is outside the window
    public class DropInput
    {
        public WorldDrop Drop { get; }
        public Window Window { get; }

        public DropInput(WorldDrop drop, Window window)
        {
            Drop = drop;
            Window = window;

            window.AllowDrop = true;
            window.PreviewDragEnter += (sender, e) => Over(e);
            window.PreviewDragOver += (sender, e) => Over(e);
            window.PreviewDragLeave += (sender, e) =>
            {
                Point at = e.GetPosition(window);
                if (at.X <= 0 || at.Y <= 0 || at.X >= window.ActualWidth || at.Y >= window.ActualHeight) Drop.Leave();
            };
            window.PreviewDrop += (sender, e) =>
            {
                if (PathsOf(e) is string[] paths) Drop.Drop(paths);
                else Drop.Leave();
                e.Handled = true;
            };
        }

        private void Over(DragEventArgs e)
        {
            IReadOnlyList<string> paths = PathsOf(e) ?? [];
            e.Effects = Drop.Enter(paths) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        // Files and folders from Explorer; anything else (text, a browser image) has none
        private static string[]? PathsOf(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
    }
}
