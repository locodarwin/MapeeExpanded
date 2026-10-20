using Mapper.Gui.Controller;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Mapper.Gui
{
    // Passes the map's mouse and key events to the markup editor. The map pans from the system-wide mouse hook, which
    // sees a press before the canvas does; while the editor holds a drag (a pin, a corner, a rectangle) panning is
    // turned off, and the map's own handler stops on the moves
    public class MarkupInput
    {
        public MarkupTool Tool { get; }
        public CanvasControl Canvas { get; }

        private bool _holding;

        public MarkupInput(MarkupTool tool, CanvasControl canvas)
        {
            Tool = tool;
            Canvas = canvas;

            canvas.MouseDown += (sender, e) =>
            {
                if (!Tool.Enabled) return;
                MarkupButton? button = e.ChangedButton == MouseButton.Left ? MarkupButton.Left : e.ChangedButton == MouseButton.Right ? MarkupButton.Right : null;
                if (button is null) return;

                Point point = e.GetPosition(canvas);
                canvas.CaptureMouse();
                if (Tool.Editor.PointerDown(point.X, point.Y, button.Value, IsShiftDown)) Hold(true);
            };
            canvas.MouseMove += (sender, e) =>
            {
                if (!Tool.Enabled) return;
                Point point = e.GetPosition(canvas);
                Tool.Editor.PointerMove(point.X, point.Y, IsShiftDown);
            };
            canvas.MouseUp += (sender, e) =>
            {
                if (!Tool.Enabled || e.ChangedButton is not (MouseButton.Left or MouseButton.Right)) return;
                Point point = e.GetPosition(canvas);
                Tool.Editor.PointerUp(point.X, point.Y, IsShiftDown);
                Hold(false);
                canvas.ReleaseMouseCapture();
            };
            canvas.MouseLeave += (sender, e) =>
            {
                if (!canvas.IsMouseCaptured) Tool.Editor.PointerLeft();
            };
            canvas.LostMouseCapture += (sender, e) => Hold(false);

            canvas.Loaded += (sender, e) =>
            {
                if (Window.GetWindow(canvas) is Window window) window.PreviewKeyDown += Window_PreviewKeyDown;
            };
        }

        private static bool IsShiftDown => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        private void Hold(bool holding)
        {
            if (_holding == holding) return;
            _holding = holding;
            Tool.Scene.Map.ScaleBehaviour.OffsetEnabled = !holding;
        }

        // Ahead of the map's own keys, but text boxes keep theirs
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!Tool.Enabled || e.OriginalSource is TextBox) return;

            bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            MarkupKey? key = e.Key switch
            {
                Key.Escape => MarkupKey.Escape,
                Key.Back => MarkupKey.Backspace,
                Key.Delete => MarkupKey.Delete,
                Key.Enter => MarkupKey.Enter,
                Key.Z when control && shift => MarkupKey.Redo,
                Key.Z when control => MarkupKey.Undo,
                Key.Y when control => MarkupKey.Redo,
                _ => null
            };

            // Undo and redo only while the bar is open, so they never surprise anyone just looking at the map
            if (key is MarkupKey.Undo or MarkupKey.Redo && !Tool.IsTurnedOn) return;
            if (key is MarkupKey k && Tool.Editor.Key(k)) e.Handled = true;
        }
    }
}
