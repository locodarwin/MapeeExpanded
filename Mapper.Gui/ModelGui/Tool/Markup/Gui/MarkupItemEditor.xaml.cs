using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

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
        private readonly List<(Button Button, Path? Glyph, string? Icon)> _icons = [];

        private static readonly Brush GlyphBrush = new SolidColorBrush(Color.FromRgb(221, 221, 221));

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
            NoteBox.Text = item.Note ?? string.Empty;
            DeleteButton.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;

            foreach (var (name, hex) in MarkupColors.All)
            {
                var (r, g, b) = MarkupColors.Parse(hex);
                Button swatch = new() { Style = (Style)FindResource("Swatch"), Background = new SolidColorBrush(Color.FromRgb(r, g, b)), ToolTip = name };
                swatch.Click += (sender, e) => PickColor(hex);
                Swatches.Children.Add(swatch);
                _swatches.Add((swatch, hex));
            }
            PickColor(item.Color);

            IconSection.Visibility = item is MapPin ? Visibility.Visible : Visibility.Collapsed;
            if (item is MapPin mapPin)
            {
                AddIcon(null, "No symbol", null);
                foreach (var (id, name, path) in PinIcons.All) AddIcon(id, name, path);
                PickIcon(mapPin.Icon);
            }

            PreviewKeyDown += OnPreviewKeyDown;
            Loaded += (sender, e) =>
            {
                LabelBox.Focus();
                Keyboard.Focus(LabelBox);
                LabelBox.SelectAll();
            };
        }

        private void AddIcon(string? id, string name, string? path)
        {
            Path? glyph = path is null ? null : new Path() { Data = Geometry.Parse("F0 " + path), Width = 15, Height = 15, Stretch = Stretch.Uniform, Fill = GlyphBrush };
            UIElement content = glyph is null ? new Ellipse() { Width = 6, Height = 6, Fill = GlyphBrush } : glyph;

            Button button = new() { Style = (Style)FindResource("IconButton"), Content = content, ToolTip = name };
            button.Click += (sender, e) => PickIcon(id);
            IconButtons.Children.Add(button);
            _icons.Add((button, glyph, id));
        }

        private void PickColor(string hex)
        {
            Item.Color = hex;
            foreach (var (button, value) in _swatches) button.Tag = value == hex ? "selected" : null;
        }

        private void PickIcon(string? icon)
        {
            if (Item is MapPin pin) pin.Icon = icon;
            foreach (var (button, glyph, value) in _icons)
            {
                button.Tag = value == icon ? "selected" : null;
                if (glyph is not null) glyph.Fill = value == icon ? Brushes.Gold : GlyphBrush;
            }
        }

        // Enter in the label saves; Escape anywhere drops the changes
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Finish(MarkupEditResult.Cancel);
            }
            else if (e.Key == Key.Enter && e.OriginalSource != NoteBox)
            {
                e.Handled = true;
                Finish(MarkupEditResult.Save);
            }
        }

        // Taking in what was typed; the popup closing without a button counts as saving too
        public void ReadFields()
        {
            Item.Label = LabelBox.Text.Trim();
            Item.Note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.TrimEnd();
        }

        private void Finish(MarkupEditResult result)
        {
            if (result == MarkupEditResult.Save) ReadFields();
            Done?.Invoke(this, result);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e) => Finish(MarkupEditResult.Save);
        private void CancelButton_Click(object sender, RoutedEventArgs e) => Finish(MarkupEditResult.Cancel);
        private void DeleteButton_Click(object sender, RoutedEventArgs e) => Finish(MarkupEditResult.Delete);
    }
}
