using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Mapper.Gui
{
    // Shown in a flyout under the Go to button; the WPF GUI uses a borderless window
    public partial class GoToWindow : UserControl
    {
        private XzPoint _playerPos, _playerSpawn, _worlSpawn;

        public XzPoint SelectedPoint { get; private set; }
        public bool DialogClosed { get; private set; } = true;

        public event EventHandler? PointSelected;

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

            CoordinateTextBox.KeyDown += (sender, e) =>
            {
                if (e.Key == Key.Enter) GoToButton_Click(sender, e);
            };
            CoordinateTextBox.TextChanged += (sender, e) => Filter();
            // Ready to type a place, whether opened by its button or Ctrl+G
            AttachedToVisualTree += (sender, e) => Dispatcher.UIThread.Post(() =>
            {
                CoordinateTextBox.Focus();
                CoordinateTextBox.SelectAll();
            }, DispatcherPriority.Input);
        }

        public void AddPlaces(IEnumerable<(string Title, XzPoint Point)> places)
        {
            foreach (var (title, point) in places)
            {
                Button button = new() { Content = $"{title} ({(int)Math.Floor(point.X)}; {(int)Math.Floor(point.Z)})" };
                button.Classes.Add("flat");
                button.Classes.Add("goto");
                button.Click += (sender, e) => Select(point);
                PlacesPanel.Children.Add(button);
                _places.Add((button, title, point));
            }
            Filter();
        }

        private void Filter()
        {
            string text = (CoordinateTextBox.Text ?? string.Empty).Trim();
            bool all = text.Length == 0 || TryParse(text, out _);
            int shown = 0;
            foreach (var (button, title, _) in _places)
            {
                button.IsVisible = all || title.Contains(text, StringComparison.OrdinalIgnoreCase);
                if (button.IsVisible) shown++;
            }
            PlacesSection.IsVisible = shown > 0;
        }

        // A place typed goes there; a name goes to the first place called that
        private void GoToButton_Click(object? sender, RoutedEventArgs e)
        {
            if (TryParse(CoordinateTextBox.Text ?? string.Empty, out XzPoint point))
            {
                Select(point);
                return;
            }
            foreach (var (button, _, place) in _places)
            {
                if (!button.IsVisible) continue;
                Select(place);
                return;
            }
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

        private void PlayerPositionButton_Click(object? sender, RoutedEventArgs e)
        {
            Select(_playerPos);
        }
        private void PlayerSpawnButton_Click(object? sender, RoutedEventArgs e)
        {
            Select(_playerSpawn);
        }
        private void WorldSpawnButton_Click(object? sender, RoutedEventArgs e)
        {
            Select(_worlSpawn);
        }

        private void Select(XzPoint point)
        {
            SelectedPoint = point;
            DialogClosed = false;

            PointSelected?.Invoke(this, EventArgs.Empty);
        }
    }
}
