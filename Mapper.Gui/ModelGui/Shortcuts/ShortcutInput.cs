using Mapper.Gui.Controller;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Mapper.Gui
{
    // Passes the main window's keys to the shortcuts, after the markup editor has had them on the way down (so Esc
    // cancels a zone being drawn before it leaves full screen), and never keys typed in a text box or a pop-up
    public class ShortcutInput
    {
        public ShortcutRunner Runner { get; }
        public ShortcutsPanel Panel { get; }
        public Window Window { get; }

        public ShortcutInput(ShortcutRunner runner, ShortcutsPanel panel, Window window)
        {
            Runner = runner;
            Panel = panel;
            Window = window;

            runner.ShowHelp = panel.Open;

            // While the key list is up, keys are for it: a new key for the one being changed, or any key closes it
            window.PreviewKeyDown += (sender, e) =>
            {
                Key key = KeyOf(e);
                ModifierKeys modifiers = Keyboard.Modifiers;
                if (!Panel.IsOpen || ShortcutRunner.IsModifier(key) || modifiers.HasFlag(ModifierKeys.Alt)) return;
                Panel.Press(ShortcutRunner.KeyOf(key), ShortcutRunner.ModifiersOf(modifiers.HasFlag(ModifierKeys.Control), modifiers.HasFlag(ModifierKeys.Shift)));
                e.Handled = true;
            };
            window.KeyDown += Window_KeyDown;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Handled || !ForTheMap(e.OriginalSource)) return;

            // No shortcut uses Alt, which opens the window's menu
            ModifierKeys modifiers = Keyboard.Modifiers;
            if (modifiers.HasFlag(ModifierKeys.Alt)) return;
            if (Runner.Run(KeyOf(e), modifiers.HasFlag(ModifierKeys.Control), modifiers.HasFlag(ModifierKeys.Shift))) e.Handled = true;
        }

        // F10 comes as a system key, as keys held with Alt do
        private static Key KeyOf(KeyEventArgs e) => e.Key == Key.System ? e.SystemKey : e.Key;

        // Keys from a pop-up (the markup popup, Places) reach the window too, through the pop-up's owner
        private bool ForTheMap(object source)
        {
            if (Keyboard.FocusedElement is TextBoxBase) return false;
            return source is not Visual visual || PresentationSource.FromVisual(visual)?.RootVisual is not Visual root || root == Window;
        }
    }
}
