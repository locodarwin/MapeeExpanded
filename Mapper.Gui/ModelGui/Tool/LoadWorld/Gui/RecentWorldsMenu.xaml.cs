using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WorldEditor;

namespace Mapper.Gui
{
    // What the Recent button shows: a row per remembered world, most recent first, with an × to take it off the list.
    // Up and Down move between rows, Enter opens one, Esc closes; RecentWorldsTool acts on what's picked
    public partial class RecentWorldsMenu : UserControl
    {
        public event EventHandler<RecentWorld>? Picked;
        public event EventHandler<RecentWorld>? Removed;
        public event EventHandler? Cleared;
        public event EventHandler? CloseRequested;

        private static readonly Brush Name = new SolidColorBrush(Color.FromRgb(224, 224, 224));
        private static readonly Brush Detail = new SolidColorBrush(Color.FromRgb(160, 160, 160));
        private readonly List<Button> _rows = [];

        public RecentWorldsMenu(IReadOnlyList<RecentWorld> worlds, string? current, DateTime nowUtc)
        {
            InitializeComponent();
            Show(worlds, current, nowUtc);

            PreviewKeyDown += (sender, e) =>
            {
                if (e.Key == Key.Escape) CloseRequested?.Invoke(this, EventArgs.Empty);
                else if (e.Key is Key.Up or Key.Down) MoveFocus(e.Key == Key.Down ? 1 : -1);
                else return;
                e.Handled = true;
            };
        }

        public void Show(IReadOnlyList<RecentWorld> worlds, string? current, DateTime nowUtc)
        {
            Rows.Children.Clear();
            _rows.Clear();

            foreach (RecentWorldRow entry in RecentWorlds.Rows(worlds.ToList(), current, nowUtc, WorldLocation.IsWorld))
            {
                RecentWorld world = entry.World;
                StackPanel text = new() { Opacity = entry.Found ? 1 : 0.5 };
                text.Children.Add(new TextBlock() { Text = entry.Title, FontSize = 13, Foreground = entry.Current ? Brushes.Gold : Name, TextTrimming = TextTrimming.CharacterEllipsis });
                text.Children.Add(new TextBlock() { Text = entry.Detail, FontSize = 11, Foreground = Detail, TextTrimming = TextTrimming.CharacterEllipsis });

                Button row = new() { Content = text, Style = (Style)FindResource("RowButton"), ToolTip = world.Path };
                row.Click += (sender, e) => Picked?.Invoke(this, world);

                Button remove = new() { Content = "×", Style = (Style)FindResource("RemoveButton"), ToolTip = "Take it off this list (the world stays where it is)" };
                remove.Click += (sender, e) => Removed?.Invoke(this, world);

                Grid line = new();
                line.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
                Grid.SetColumn(remove, 1);
                line.Children.Add(row);
                line.Children.Add(remove);
                Rows.Children.Add(line);
                _rows.Add(row);
            }

            EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ClearSection.Visibility = _rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (IsVisible && _rows.Count > 0 && !_rows.Any(r => r.IsKeyboardFocused)) Keyboard.Focus(_rows[0]);
        }

        public void FocusFirst()
        {
            if (_rows.Count > 0) Keyboard.Focus(_rows[0]);
        }

        private void MoveFocus(int by)
        {
            if (_rows.Count == 0) return;
            int at = _rows.FindIndex(r => r.IsKeyboardFocused);
            Keyboard.Focus(_rows[Math.Clamp(at < 0 ? 0 : at + by, 0, _rows.Count - 1)]);
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e) => Cleared?.Invoke(this, EventArgs.Empty);
    }
}
