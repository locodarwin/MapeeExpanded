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
            // No shortcut uses Alt, and the window manager may
            if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return;
            bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

            // While the key list is up, keys are for it: a new key for the one being changed, or any key closes it
            if (Panel.IsOpen)
            {
                if (ShortcutRunner.IsModifier(e.Key)) return;
                Panel.Press(ShortcutRunner.KeyOf(e.Key), ShortcutRunner.ModifiersOf(control, shift));
                e.Handled = true;
                return;
            }
            if (e.Handled || !ForTheMap(e.Source)) return;

            if (Runner.Run(e.Key, control, shift)) e.Handled = true;
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
