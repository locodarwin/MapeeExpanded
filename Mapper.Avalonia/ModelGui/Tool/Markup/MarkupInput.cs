using Mapper.Gui.Controller;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Mapper.Gui
{
    // Passes the map's pointer and key events to the markup editor. While the editor holds a drag (a pin, a corner, a
    // rectangle) the map doesn't pan; the map's own handler has already seen the press, so it's stopped on the moves
    public class MarkupInput
    {
        public MarkupTool Tool { get; }
        public CanvasControl Canvas { get; }

        private bool _holding;

        public MarkupInput(MarkupTool tool, CanvasControl canvas)
        {
            Tool = tool;
            Canvas = canvas;

            canvas.PointerPressed += (sender, e) =>
            {
                if (!Tool.Enabled) return;
                PointerPointProperties properties = e.GetCurrentPoint(canvas).Properties;
                MarkupButton? button = properties.IsLeftButtonPressed ? MarkupButton.Left : properties.IsRightButtonPressed ? MarkupButton.Right : null;
                if (button is null) return;

                var point = e.GetPosition(canvas);
                if (Tool.Editor.PointerDown(point.X, point.Y, button.Value, e.KeyModifiers.HasFlag(KeyModifiers.Shift))) Hold(true);
            };
            canvas.PointerMoved += (sender, e) =>
            {
                if (!Tool.Enabled) return;
                var point = e.GetPosition(canvas);
                Tool.Editor.PointerMove(point.X, point.Y, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            };
            canvas.PointerReleased += (sender, e) =>
            {
                if (!Tool.Enabled) return;
                var point = e.GetPosition(canvas);
                Tool.Editor.PointerUp(point.X, point.Y, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                Hold(false);
            };
            canvas.PointerExited += (sender, e) => Tool.Editor.PointerLeft();
            canvas.PointerCaptureLost += (sender, e) => Hold(false);

            canvas.AttachedToVisualTree += (sender, e) =>
            {
                if (TopLevel.GetTopLevel(canvas) is TopLevel window) window.AddHandler(InputElement.KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
            };
        }

        private void Hold(bool holding)
        {
            if (_holding == holding) return;
            _holding = holding;
            Tool.Scene.Map.ScaleBehaviour.OffsetEnabled = !holding;
        }

        // Ahead of the map's own keys, but text boxes (the markup popup's among them) keep theirs
        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            if (!Tool.Enabled || e.Source is TextBox) return;

            bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            MarkupKey? key = e.Key switch
            {
                Key.Escape => MarkupKey.Escape,
                Key.Back => MarkupKey.Backspace,
                Key.Delete => MarkupKey.Delete,
                Key.Enter => MarkupKey.Enter,
                Key.Z when control && e.KeyModifiers.HasFlag(KeyModifiers.Shift) => MarkupKey.Redo,
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
