using Mapper.Gui.Controller;
using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Mapper.Gui
{
    // What the Overlays button shows: a row per overlay with a tick, its icon and its key. A click or Enter turns it
    // on or off and the menu stays open, so several can be changed at once; Up and Down move, Esc closes
    public partial class OverlaysMenu : UserControl
    {
        public event EventHandler? CloseRequested;

        private static readonly Brush Text = new SolidColorBrush(Color.FromRgb(224, 224, 224));
        private static readonly Brush Detail = new SolidColorBrush(Color.FromRgb(160, 160, 160));
        private static readonly Brush Gold = new SolidColorBrush(ToolButtonHook.OnInk);
        private static readonly Brush Box = new SolidColorBrush(Color.FromRgb(120, 120, 120));
        private static readonly Brush Dark = new SolidColorBrush(Color.FromRgb(28, 28, 28));

        private readonly List<Button> _rows = [];

        public OverlaysMenu(IReadOnlyList<OverlayItem> items, ShortcutKeys keys)
        {
            InitializeComponent();

            foreach (OverlayItem item in items) Rows.Children.Add(CreateRow(item, keys));

            PreviewKeyDown += (sender, e) =>
            {
                if (e.Key == Key.Escape) CloseRequested?.Invoke(this, EventArgs.Empty);
                else if (e.Key is Key.Up or Key.Down) MoveFocus(e.Key == Key.Down ? 1 : -1);
                else return;
                e.Handled = true;
            };
        }

        private Button CreateRow(OverlayItem item, ShortcutKeys keys)
        {
            Border tick = new()
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = ToolIconView.Create(ToolIcons.Check, Dark, 12)
            };
            FrameworkElement icon = ToolIconView.Create(item.Icon, Text, 18);
            icon.Margin = new Thickness(10, 0, 8, 0);
            TextBlock label = new() { Text = item.Label, FontSize = 13, Foreground = Text, VerticalAlignment = VerticalAlignment.Center };
            TextBlock key = new() { Text = keys.Map.KeyText(item.Action) ?? string.Empty, FontSize = 12, Foreground = Detail, VerticalAlignment = VerticalAlignment.Center };

            Grid line = new();
            line.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            Grid.SetColumn(icon, 1);
            Grid.SetColumn(label, 2);
            Grid.SetColumn(key, 3);
            line.Children.Add(tick);
            line.Children.Add(icon);
            line.Children.Add(label);
            line.Children.Add(key);

            Button row = new() { Content = line, Style = (Style)FindResource("RowButton") };
            row.Click += (sender, e) =>
            {
                if (item.Tool.Enabled) item.Tool.IsTurnedOn = !item.Tool.IsTurnedOn;
            };

            void Show()
            {
                bool on = item.Tool.IsTurnedOn;
                tick.Background = on ? Gold : Brushes.Transparent;
                tick.BorderBrush = on ? Gold : Box;
                tick.Child.Visibility = on ? Visibility.Visible : Visibility.Hidden;
                row.Opacity = item.Tool.Enabled ? 1 : 0.5;
            }
            Show();
            item.Tool.OnTurnedOn += isOn => Show();
            item.Tool.OnEnabled += isEnabled => Show();

            _rows.Add(row);
            return row;
        }

        public void FocusFirst()
        {
            if (_rows.Count > 0) Keyboard.Focus(_rows[0]);
        }

        private void MoveFocus(int by)
        {
            int at = _rows.FindIndex(r => r.IsKeyboardFocused);
            Keyboard.Focus(_rows[Math.Clamp(at < 0 ? 0 : at + by, 0, _rows.Count - 1)]);
        }
    }
}
