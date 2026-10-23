using Mapper.Gui.Controller;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using WorldEditor;

namespace Mapper.Gui
{
    // What the Open button shows: Browse, then a row per remembered world, most recent first, with an × to take it off
    // the list. Up and Down move between rows, Enter opens one, Esc closes; RecentWorldsTool acts on what's picked
    public partial class RecentWorldsMenu : UserControl
    {
        public event EventHandler<RecentWorld>? Picked;
        public event EventHandler<RecentWorld>? Removed;
        public event EventHandler? Cleared;
        public event EventHandler? CloseRequested;
        public event EventHandler? BrowseRequested;

        private static readonly IBrush Name = new SolidColorBrush(Color.FromRgb(224, 224, 224));
        private static readonly IBrush Detail = new SolidColorBrush(Color.FromRgb(160, 160, 160));
        private readonly List<Button> _rows = [];

        public RecentWorldsMenu(IReadOnlyList<RecentWorld> worlds, string? current, DateTime nowUtc, string? browseKey = null)
        {
            InitializeComponent();
            BrowseKey.Text = browseKey ?? string.Empty;
            Show(worlds, current, nowUtc);

            AddHandler(KeyDownEvent, (sender, e) =>
            {
                if (e.Key == Key.Escape) CloseRequested?.Invoke(this, EventArgs.Empty);
                else if (e.Key is Key.Up or Key.Down) MoveFocus(e.Key == Key.Down ? 1 : -1);
                else return;
                e.Handled = true;
            }, RoutingStrategies.Tunnel);
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

                Button row = new() { Content = text };
                row.Classes.Add("flat");
                row.Classes.Add("recent");
                ToolTip.SetTip(row, world.Path);
                row.Click += (sender, e) => Picked?.Invoke(this, world);

                Button remove = new() { Content = "×" };
                remove.Classes.Add("flat");
                remove.Classes.Add("remove");
                ToolTip.SetTip(remove, "Take it off this list (the world stays where it is)");
                remove.Click += (sender, e) => Removed?.Invoke(this, world);

                Grid line = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                Grid.SetColumn(remove, 1);
                line.Children.Add(row);
                line.Children.Add(remove);
                Rows.Children.Add(line);
                _rows.Add(row);
            }

            EmptyText.IsVisible = _rows.Count == 0;
            ClearSection.IsVisible = _rows.Count > 0;
            if (IsVisible && _rows.Count > 0 && !_rows.Any(r => r.IsFocused)) _rows[0].Focus();
        }

        // The latest world, so Enter reopens it; Browse when there's none
        public void FocusFirst()
        {
            (_rows.Count > 0 ? _rows[0] : BrowseButton).Focus();
        }

        private void MoveFocus(int by)
        {
            List<Button> rows = [BrowseButton, .. _rows];
            int at = rows.FindIndex(r => r.IsFocused);
            rows[Math.Clamp(at < 0 ? 0 : at + by, 0, rows.Count - 1)].Focus();
        }

        private void BrowseButton_Click(object? sender, RoutedEventArgs e) => BrowseRequested?.Invoke(this, EventArgs.Empty);

        private void ClearButton_Click(object? sender, RoutedEventArgs e) => Cleared?.Invoke(this, EventArgs.Empty);
    }
}
