using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Mapper.Gui
{
    // Shown in a flyout under the Go to button; the WPF GUI uses a borderless window
    public partial class GoToWindow : UserControl
    {
        private XzPoint _playerPos, _playerSpawn, _worlSpawn;

        public XzPoint SelectedPoint { get; private set; }
        public bool DialogClosed { get; private set; } = true;

        public event EventHandler? PointSelected;

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
            }
            PlacesSection.IsVisible = PlacesPanel.Children.Count > 0;
        }

        private void GoToButton_Click(object? sender, RoutedEventArgs e)
        {
            if (!TryParse(CoordinateTextBox.Text ?? string.Empty, out XzPoint point)) return;
            Select(point);
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
