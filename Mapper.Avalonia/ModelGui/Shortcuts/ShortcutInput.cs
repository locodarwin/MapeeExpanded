using Mapper.Gui.Controller;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Mapper.Gui
{
    // Passes the main window's keys to the shortcuts, after the markup editor has had them (so Esc cancels a zone
    // being drawn before it does anything else), and never keys typed in a text box or a pop-up
    public class ShortcutInput
    {
        public ShortcutRunner Runner { get; }
        public ShortcutsPanel Panel { get; }
        public TopLevel Window { get; }

        public ShortcutInput(ShortcutRunner runner, ShortcutsPanel panel, TopLevel window)
        {
            Runner = runner;
            Panel = panel;
            Window = window;

            runner.ShowHelp = panel.Open;

            // On the way down to the focused control rather than back up from it: Avalonia's own arrow-key focus
            // moving would otherwise take the first arrow press. The markup editor's handler was added first, and a
            // key it used (Esc on a zone being drawn) is already handled here
            window.AddHandler(InputElement.KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
        }

        private void Window_KeyDown(object? sender, KeyEventArgs e)
        {
            // While the key list is up, any key only closes it
            if (Panel.IsOpen)
            {
                if (e.Key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl) return;
                Panel.Close();
                e.Handled = true;
                return;
            }
            if (e.Handled || !ForTheMap(e.Source)) return;

            bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (Runner.Run(e.Key, control, e.KeyModifiers.HasFlag(KeyModifiers.Shift))) e.Handled = true;
        }

        private bool ForTheMap(object? source)
        {
            if (source is not Visual visual) return true;
            if (visual is TextBox || visual.FindAncestorOfType<TextBox>() is not null) return false;
            if (TopLevel.GetTopLevel(visual) != Window) return false;
            return visual.FindAncestorOfType<OverlayPopupHost>() is null;
        }
    }
}
