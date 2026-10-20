using Mapper.Gui.Controller;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Mapper.Gui
{
    // Feeds files dragged onto the main window to WorldDrop. Leaving one control for another inside the window raises a
    // leave and an enter on the way, so a leave only counts once the pointer is outside the window
    public class DropInput
    {
        public WorldDrop Drop { get; }
        public Window Window { get; }

        public DropInput(WorldDrop drop, Window window)
        {
            Drop = drop;
            Window = window;

            DragDrop.SetAllowDrop(window, true);
            window.AddHandler(DragDrop.DragEnterEvent, (sender, e) => Over(e));
            window.AddHandler(DragDrop.DragOverEvent, (sender, e) => Over(e));
            window.AddHandler(DragDrop.DragLeaveEvent, (sender, e) =>
            {
                Point at = e.GetPosition(window);
                if (at.X <= 0 || at.Y <= 0 || at.X >= window.Bounds.Width || at.Y >= window.Bounds.Height) Drop.Leave();
            });
            window.AddHandler(DragDrop.DropEvent, (sender, e) =>
            {
                List<string>? paths = PathsOf(e);
                if (paths is null) Drop.Leave();
                else Drop.Drop(paths);
                e.Handled = true;
            });
        }

        private void Over(DragEventArgs e)
        {
            e.DragEffects = Drop.Enter(PathsOf(e)) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        // Only files on this machine have a path; null when the drag hasn't said yet what it holds
        private static List<string>? PathsOf(DragEventArgs e)
        {
            IStorageItem[]? items = e.DataTransfer.TryGetFiles();
            return items?.Select(item => item.TryGetLocalPath()).OfType<string>().ToList();
        }
    }
}
