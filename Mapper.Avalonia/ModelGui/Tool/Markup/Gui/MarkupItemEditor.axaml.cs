using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Mapper.Gui
{
    public enum MarkupEditResult { Save, Cancel, Delete }

    // A pin or zone's details, shown in a popup beside it: label, colour, a pin's symbol, and a note. It edits the item
    // it's given and says how it was closed; what then happens to the map is the editor's business
    public partial class MarkupItemEditor : UserControl
    {
        public MarkupItem Item { get; }
        public bool IsNew { get; }

        public event EventHandler<MarkupEditResult>? Done;

        private readonly List<(Button Button, string Hex)> _swatches = [];
        private readonly List<(Button Button, string? Icon)> _icons = [];

        public MarkupItemEditor(MarkupItem item, bool isNew)
        {
            InitializeComponent();

            Item = item;
            IsNew = isNew;

            string kind = item is MapPin ? "pin" : "zone";
            TitleText.Text = isNew ? $"New {kind}" : char.ToUpperInvariant(kind[0]) + kind[1..];
            WhereText.Text = item switch
            {
                MapPin pin => $"{pin.X}; {pin.Z}",
                MapZone zone => ZoneGeometry.Describe(zone.Points),
                _ => string.Empty
            };
            LabelBox.Text = item.Label;
            LabelBox.PlaceholderText = item is MapPin ? "Home, mine, portal..." : "Town, farm, claim...";
            NoteBox.Text = item.Note;
            DeleteButton.IsVisible = !isNew;

            foreach (var (name, hex) in MarkupColors.All)
            {
                var (r, g, b) = MarkupColors.Parse(hex);
                Button swatch = new() { Background = new SolidColorBrush(Color.FromRgb(r, g, b)) };
                swatch.Classes.Add("swatch");
                ToolTip.SetTip(swatch, name);
                swatch.Click += (sender, e) => PickColor(hex);
                Swatches.Children.Add(swatch);
                _swatches.Add((swatch, hex));
            }
            PickColor(item.Color);

            IconSection.IsVisible = item is MapPin;
            if (item is MapPin mapPin)
            {
                AddIcon(null, "No symbol", new Ellipse() { Width = 6, Height = 6, Fill = new SolidColorBrush(Color.FromRgb(221, 221, 221)) });
                foreach (var (id, name, path) in PinIcons.All)
                {
                    AddIcon(id, name, new Path() { Data = Geometry.Parse("F0 " + path), Width = 15, Height = 15, Stretch = Stretch.Uniform });
                }
                PickIcon(mapPin.Icon);
            }

            AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            AttachedToVisualTree += (sender, e) =>
            {
                LabelBox.Focus();
                LabelBox.SelectAll();
            };
        }

        private void AddIcon(string? id, string name, Control content)
        {
            Button button = new() { Content = content, Padding = new(0), HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center };
            button.Classes.Add("icon");
            ToolTip.SetTip(button, name);
            button.Click += (sender, e) => PickIcon(id);
            IconButtons.Children.Add(button);
            _icons.Add((button, id));
        }

        private void PickColor(string hex)
        {
            Item.Color = hex;
            foreach (var (button, value) in _swatches) button.Classes.Set("selected", value == hex);
        }

        private void PickIcon(string? icon)
        {
            if (Item is MapPin pin) pin.Icon = icon;
            foreach (var (button, value) in _icons) button.Classes.Set("selected", value == icon);
        }

        // Enter in the label saves; Escape anywhere drops the changes. Taken before the popup sees Escape, which
        // would otherwise just close it
        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Finish(MarkupEditResult.Cancel);
            }
            else if (e.Key == Key.Enter && e.Source is not TextBox { AcceptsReturn: true })
            {
                e.Handled = true;
                Finish(MarkupEditResult.Save);
            }
        }

        // Taking in what was typed; the popup closing without a button counts as saving too
        public void ReadFields()
        {
            Item.Label = (LabelBox.Text ?? string.Empty).Trim();
            Item.Note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.TrimEnd();
        }

        private void Finish(MarkupEditResult result)
        {
            if (result == MarkupEditResult.Save) ReadFields();
            Done?.Invoke(this, result);
        }

        private void SaveButton_Click(object? sender, RoutedEventArgs e) => Finish(MarkupEditResult.Save);
        private void CancelButton_Click(object? sender, RoutedEventArgs e) => Finish(MarkupEditResult.Cancel);
        private void DeleteButton_Click(object? sender, RoutedEventArgs e) => Finish(MarkupEditResult.Delete);
    }
}
