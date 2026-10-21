using System.Collections.Generic;
using System.Linq;
#if AVALONIA
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Element = Avalonia.Controls.Control;
#else
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Element = System.Windows.UIElement;
#endif

namespace Mapper.Gui
{
    // The key list (F1 or ?), over the whole map. Clicking a key waits for a new one to take its place; any other key,
    // or a click outside the card, closes it. The GUI's key handling passes the keys here while it's open
    public class ShortcutsPanel : UserControl
    {
        private static readonly SolidColorBrush Text = new(Color.FromRgb(221, 221, 221));
        private static readonly SolidColorBrush Detail = new(Color.FromRgb(170, 170, 170));
        private static readonly SolidColorBrush Heading = new(Color.FromRgb(255, 215, 0));
        private static readonly SolidColorBrush KeyBack = new(Color.FromRgb(48, 48, 48));
        private static readonly SolidColorBrush KeyEdge = new(Color.FromRgb(90, 90, 90));
        private static readonly SolidColorBrush Waiting = new(Color.FromRgb(92, 76, 0));
        private static readonly SolidColorBrush Transparent = new(Color.FromArgb(0, 0, 0, 0));
#if AVALONIA
        private static readonly FontWeight Bold = FontWeight.Bold;
#else
        private static readonly FontWeight Bold = FontWeights.Bold;
#endif

        public ShortcutKeys Keys { get; }
        public bool IsOpen { get; private set; }
        // The action waiting for its new key, while one is
        public ShortcutAction? Editing { get; private set; }
        // What the last change did, or why it wasn't made
        public string? Message { get; private set; }

        private readonly bool _fullScreen;
        private readonly Border _backdrop;
        private readonly Border _card;

        // fullScreen: whether this GUI has a full-screen key
        public ShortcutsPanel(ShortcutKeys keys, bool fullScreen)
        {
            Keys = keys;
            _fullScreen = fullScreen;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;

            _card = new Border()
            {
                Background = new SolidColorBrush(Color.FromRgb(28, 28, 28)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(112, 112, 112)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20)
            };
            // The map dimmed behind the card, which also takes the click that closes it
            _backdrop = new Border() { Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), Child = _card, Tag = "outside" };
            _card.Tag = "card";
            Content = _backdrop;

            // A click on the card itself only stops waiting for a key
            OnClick(_card, () =>
            {
                if (Editing is null) return;
                Editing = null;
                Fill();
            }, hand: false);
#if AVALONIA
            IsVisible = false;
            _backdrop.PointerPressed += (sender, e) =>
            {
                if (e.Source != _backdrop) return;
                Close();
                e.Handled = true;
            };
#else
            Visibility = Visibility.Collapsed;
            _backdrop.MouseDown += (sender, e) =>
            {
                if (e.OriginalSource != _backdrop) return;
                Close();
                e.Handled = true;
            };
#endif

            keys.Changed += Fill;
            Fill();
        }

        // A key pressed while the list is up. Modifier keys alone never reach here
        public void Press(ShortcutKey key, ShortcutModifiers modifiers)
        {
            if (Editing is not ShortcutAction action)
            {
                Close();
                return;
            }
            if (key == ShortcutKey.Escape)
            {
                Editing = null;
                Message = null;
                Fill();
                return;
            }

            KeyChord chord = new(key, modifiers);
            if (ShortcutKeys.Refusal(chord) is string refusal)
            {
                Message = refusal;
                Fill();
                return;
            }

            Editing = null;
            Message = Describe(action, Keys.Assign(action, chord));
            Fill();
        }

        public void StartEditing(ShortcutAction action)
        {
            Editing = action;
            Message = null;
            Fill();
        }

        public void Reset(ShortcutAction action)
        {
            Editing = null;
            Message = Describe(action, Keys.Reset(action));
            Fill();
        }

        public void ResetAll()
        {
            Editing = null;
            Message = "Every key is back to how it came";
            Keys.ResetAll();
            Fill();
        }

        // "Axes is now G. It came from Grid lines. Grid lines now has no key."
        private string Describe(ShortcutAction action, ShortcutChange change)
        {
            if (change.Refused is string refused) return refused;

            string keys = Keys.Map.KeysText(action);
            string text = keys.Length == 0 ? $"{KeyMap.Name(action)} has no key." : $"{KeyMap.Name(action)} is now {keys}.";
            List<ShortcutAction> lost = [.. change.Lost.Where(Shown)];
            if (lost.Count == 0) return text;

            List<string> none = [.. lost.Where(a => Keys.Map.KeysText(a).Length == 0).Select(KeyMap.Name)];
            text += $" It came from {And(lost.Select(KeyMap.Name))}.";
            if (none.Count > 0) text += $" {And(none)} now {(none.Count == 1 ? "has" : "have")} no key.";
            foreach (ShortcutAction kept in lost.Where(a => Keys.Map.KeysText(a).Length > 0)) text += $" {KeyMap.Name(kept)} keeps {Keys.Map.KeysText(kept)}.";
            return text;
        }

        private static string And(IEnumerable<string> names)
        {
            List<string> list = [.. names];
            return list.Count <= 1 ? string.Concat(list) : string.Join(", ", list[..^1]) + " and " + list[^1];
        }

        private bool Shown(ShortcutAction action) => _fullScreen || action != ShortcutAction.FullScreen;

        private void Fill()
        {
            WrapPanel columns = new() { Orientation = Orientation.Horizontal, MaxWidth = 960 };
            foreach (ShortcutHelpGroup group in Keys.Map.Help(_fullScreen)) columns.Children.Add(Group(group));

            string hint = Editing is ShortcutAction editing
                ? $"Press the new key for {KeyMap.Name(editing)}, with Ctrl or Shift if you like. Esc cancels."
                : "Click a key to change it. None of these work while typing in a box. Any other key, or a click outside, closes this.";

            StackPanel card = new();
            card.Children.Add(new TextBlock() { Text = "Keyboard shortcuts", FontSize = 16, Foreground = Text, Margin = new Thickness(14, 10, 14, 2) });
            card.Children.Add(new TextBlock() { Text = hint, FontSize = 11, Foreground = Detail, Margin = new Thickness(14, 0, 14, 4) });
            // Kept when empty, so the card doesn't jump as messages come and go
            card.Children.Add(new TextBlock() { Text = Message ?? " ", FontSize = 11, Foreground = Heading, Margin = new Thickness(14, 0, 14, 4), TextWrapping = TextWrapping.Wrap, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left });
            card.Children.Add(columns);
            if (Keys.AnyChanged)
            {
                TextBlock all = Link("Reset all keys", ResetAll);
                all.Tag = "reset-all";
                all.Margin = new Thickness(14, 0, 14, 10);
                all.HorizontalAlignment = HorizontalAlignment.Left;
                card.Children.Add(all);
            }
            _card.Child = card;
        }

        private Grid Group(ShortcutHelpGroup group)
        {
            Grid grid = new() { Margin = new Thickness(14, 8, 14, 10), VerticalAlignment = VerticalAlignment.Top };
            grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });

            void Add(Element control, int column)
            {
                Grid.SetRow(control, grid.RowDefinitions.Count - 1);
                Grid.SetColumn(control, column);
                grid.Children.Add(control);
            }

            grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            TextBlock title = new() { Text = group.Title, FontSize = 12, Foreground = Heading, Margin = new Thickness(0, 0, 0, 4) };
            Grid.SetColumnSpan(title, 3);
            Add(title, 0);

            foreach (ShortcutHelp line in group.Lines)
            {
                grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
                Add(KeyCell(line), 0);
                Add(new TextBlock() { Text = line.Description, FontSize = 12, Foreground = Detail, Margin = new Thickness(0, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center }, 1);
                if (line.Action is ShortcutAction action && Keys.IsChanged(action))
                {
                    TextBlock reset = Link("Reset", () => Reset(action));
                    reset.VerticalAlignment = VerticalAlignment.Center;
                    reset.Tag = $"reset:{action}";
                    Add(reset, 2);
                }
            }
            return grid;
        }

        // The keys of a line; a box to click when they can be changed
        private Element KeyCell(ShortcutHelp line)
        {
            bool waiting = line.Action is not null && line.Action == Editing;
            string keys = waiting ? "Press a key" : line.Keys.Length > 0 ? line.Keys : "none";
            TextBlock text = new() { Text = keys, FontSize = 12, FontWeight = Bold, Foreground = line.Keys.Length > 0 || waiting ? Text : Detail };
            if (line.Action is not ShortcutAction action)
            {
                text.Margin = new Thickness(0, 2, 14, 2);
                return text;
            }

            Border box = new()
            {
                Child = text,
                Background = waiting ? Waiting : KeyBack,
                BorderBrush = waiting ? Heading : KeyEdge,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 0, 5, 0),
                Margin = new Thickness(0, 1, 10, 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 26,
                Tag = action.ToString()
            };
            OnClick(box, () => StartEditing(action));
            return box;
        }

        private TextBlock Link(string text, System.Action clicked)
        {
            TextBlock link = new() { Text = text, FontSize = 11, Foreground = Heading, TextDecorations = TextDecorations.Underline, Background = Transparent };
            OnClick(link, clicked);
            return link;
        }

        private static void OnClick(Element element, System.Action clicked, bool hand = true)
        {
#if AVALONIA
            if (hand) element.Cursor = new Cursor(StandardCursorType.Hand);
            element.PointerPressed += (sender, e) =>
            {
                clicked();
                e.Handled = true;
            };
#else
            if (hand && element is FrameworkElement framework) framework.Cursor = Cursors.Hand;
            element.MouseLeftButtonDown += (sender, e) =>
            {
                clicked();
                e.Handled = true;
            };
#endif
        }

        public void Open()
        {
            IsOpen = true;
            Editing = null;
            Message = null;
            Fill();
#if AVALONIA
            IsVisible = true;
#else
            Visibility = Visibility.Visible;
#endif
        }

        public void Close()
        {
            IsOpen = false;
            Editing = null;
#if AVALONIA
            IsVisible = false;
#else
            Visibility = Visibility.Collapsed;
#endif
        }
    }
}
