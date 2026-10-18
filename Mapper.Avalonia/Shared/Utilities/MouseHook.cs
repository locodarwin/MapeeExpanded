using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;

namespace Mapper.Gui
{
    // Left-button presses, moves and releases on the map canvas, in canvas coordinates. The WPF GUI hooks the
    // mouse system-wide; here pointer capture keeps the moves coming while a drag leaves the canvas
    public static class MouseHook
    {
        public static event EventHandler<Point> MouseDown = delegate { };
        public static event EventHandler<Point> MouseUp = delegate { };
        public static event EventHandler<Point> MouseMove = delegate { };

        public static void Start(Control control)
        {
            control.PointerPressed += (sender, e) =>
            {
                if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;
                MouseDown?.Invoke(null, e.GetPosition(control));
            };
            control.PointerMoved += (sender, e) => MouseMove?.Invoke(null, e.GetPosition(control));
            control.PointerReleased += (sender, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                MouseUp?.Invoke(null, e.GetPosition(control));
            };
        }
    }
}
