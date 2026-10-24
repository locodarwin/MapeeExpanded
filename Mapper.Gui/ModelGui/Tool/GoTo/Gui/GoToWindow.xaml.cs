using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Mapper.Gui
{
    /// <summary>
    /// Interaction logic for GoToWindow.xaml
    /// </summary>
    public partial class GoToWindow : Window
    {
        private XzPoint _playerPos, _playerSpawn, _worlSpawn;

        public XzPoint SelectedPoint { get; private set; }
        public bool DialogClosed { get; private set; } = true;

        private bool _closing = false;
        // Typing a name rather than a place shows only the places called that
        private readonly List<(Button Button, string Title, XzPoint Point)> _places = [];

        public GoToWindow(XzPoint selected, XzPoint playerPosition, XzPoint playerSpawn, XzPoint worldSpawn)
        {
            InitializeComponent();

            selected.X = Math.Ceiling(selected.X);
            selected.Z = Math.Ceiling(selected.Z);

            SelectedPoint = selected;
            _playerPos = playerPosition;
            _playerSpawn = playerSpawn;
            _worlSpawn = worldSpawn;

            CoordinateTextBox.Text = $"{selected}";
            PlayerPositionButton.Content = $"Player position ({playerPosition})";
            PlayerSpawnButton.Content = $"Player spawn ({playerSpawn})";
            WorldSpawnButton.Content = $"World spawn ({worldSpawn})";

            // Ready to type a place, whether opened by its button or Ctrl+G; Enter goes there and Esc closes it
            Loaded += (sender, e) =>
            {
                CoordinateTextBox.Focus();
                CoordinateTextBox.SelectAll();
            };
            CoordinateTextBox.TextChanged += (sender, e) => Filter();
            PreviewKeyDown += (sender, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    _closing = true;
                    Close();
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter && e.OriginalSource == CoordinateTextBox)
                {
                    GoToButton_Click(CoordinateTextBox, e);
                    e.Handled = true;
                }
            };
        }

        // Buttons like the spawn points' for each of the world's pins and zones, and its structures
        public void AddPlaces(IEnumerable<(string Title, XzPoint Point)> places)
        {
            foreach (var (title, point) in places)
            {
                Button button = new()
                {
                    Content = $"{title} ({(int)Math.Floor(point.X)}; {(int)Math.Floor(point.Z)})",
                    Style = WorldSpawnButton.Style,
                    Foreground = WorldSpawnButton.Foreground,
                    BorderBrush = WorldSpawnButton.BorderBrush,
                    Height = 25
                };
                button.Click += (sender, e) => Select(point);
                PlacesPanel.Children.Add(button);
                _places.Add((button, title, point));
            }
            Filter();
        }

        private void Filter()
        {
            string text = CoordinateTextBox.Text.Trim();
            bool all = text.Length == 0 || TryParse(text, out _);
            int shown = 0;
            foreach (var (button, title, _) in _places)
            {
                bool show = all || title.Contains(text, StringComparison.OrdinalIgnoreCase);
                button.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show) shown++;
            }
            PlacesSection.Visibility = shown > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // A place typed goes there; a name goes to the first place called that
        private void GoToButton_Click(object sender, RoutedEventArgs e)
        {
            if (TryParse(CoordinateTextBox.Text, out XzPoint point))
            {
                Select(point);
                return;
            }
            foreach (var (button, _, place) in _places)
            {
                if (button.Visibility != Visibility.Visible) continue;
                Select(place);
                return;
            }
        }

        private void Select(XzPoint point)
        {
            SelectedPoint = point;
            DialogClosed = false;

            _closing = true;
            Close();
        }

        private static bool TryParse(string input, out XzPoint point)
        {
            try
            {
                string[] split = input.Split(";");

                point = new XzPoint(int.Parse(split[0].Trim()), int.Parse(split[1].Trim()));
                return true;
            }
            catch
            {
                point = new XzPoint();
                return false;
            }
        }

        private void PlayerPositionButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedPoint = _playerPos;
            DialogClosed = false;

            _closing = true;
            Close();
        }
        private void PlayerSpawnButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedPoint = _playerSpawn;
            DialogClosed = false;

            _closing = true;
            Close();
        }
        private void WorldSpawnButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedPoint = _worlSpawn;
            DialogClosed = false;

            _closing = true;
            Close();
        }

        // However it closes (its own buttons, Esc, the frame's close button, or the app), losing focus on the way
        // out mustn't close it a second time, which WPF refuses with an exception
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _closing = true;
            base.OnClosing(e);
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            if (!_closing) Close();
        }
    }
}
