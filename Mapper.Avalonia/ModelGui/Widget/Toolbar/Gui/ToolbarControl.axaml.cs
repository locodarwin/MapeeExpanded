using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Mapper.Gui
{
    // The buttons in their groups, each an icon with a label under it. Where the window is too narrow for the labels
    // beside the zoom box and the dimension buttons, or they're turned off (right-click), the buttons are icons only
    public partial class ToolbarControl : UserControl
    {
        public IToolbarWidget Toolbar { get; }

        // Room the bar leaves either side of itself, together, for the zoom box and the dimension buttons
        private const double Beside = 270;
        private const double LabelledHeight = 50, IconHeight = 34;

        // The badge's holder is hidden with the labels: on an icon-only button it would cover the icon, and gold says
        // as much
        private readonly List<(Button Button, Control Label, Control Badge)> _buttons = [];
        private readonly List<Border> _separators = [];
        private double _labelledWidth;
        private bool? _labels;
        private Control? _watched;

        public ToolbarControl(IToolbarWidget toolbar)
        {
            InitializeComponent();

            Toolbar = toolbar;
            LoadSegments();
            CreateContextMenu();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            if (_watched is not null) _watched.SizeChanged -= Watched_SizeChanged;
            _watched = Parent as Control;
            if (_watched is not null) _watched.SizeChanged += Watched_SizeChanged;
            Fit();
        }

        private void Watched_SizeChanged(object? sender, SizeChangedEventArgs e) => Fit();

        public void Fit()
        {
            if (_labelledWidth == 0)
            {
                ShowLabels(true);
                Measure(Size.Infinity);
                _labelledWidth = DesiredSize.Width;
            }

            double room = _watched?.Bounds.Width ?? double.PositiveInfinity;
            ShowLabels(Toolbar.ShowLabels && room >= _labelledWidth + Beside);
        }

        private void ShowLabels(bool labels)
        {
            if (_labels == labels) return;
            _labels = labels;

            foreach (var (button, label, badge) in _buttons)
            {
                label.IsVisible = labels;
                button.Height = labels ? LabelledHeight : IconHeight;
                button.MinWidth = labels ? 54 : IconHeight;
                badge.IsVisible = labels;
            }
            foreach (Border separator in _separators) separator.Height = labels ? 30 : 22;
        }

        private void CreateContextMenu()
        {
            MenuItem item = new() { Header = "Show labels under the icons", ToggleType = MenuItemToggleType.CheckBox, IsChecked = Toolbar.ShowLabels };
            item.Click += (sender, e) =>
            {
                Toolbar.ShowLabels = !Toolbar.ShowLabels;
                item.IsChecked = Toolbar.ShowLabels;
                Fit();
            };
            ContextMenu = new ContextMenu() { Items = { item } };
        }

        private void LoadSegments()
        {
            for (int i = 0; i < Toolbar.ToolButtonSegments.Count; i++)
            {
                if (i > 0) AddSeparator();
                foreach (IToolButton button in Toolbar.ToolButtonSegments[i].Tools) ButtonContainer.Children.Add(CreateButton(button));
            }
        }

        private void AddSeparator()
        {
            Border separator = new()
            {
                Width = 1,
                Background = new SolidColorBrush(Color.FromRgb(71, 71, 71)),
                Margin = new Thickness(5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _separators.Add(separator);
            ButtonContainer.Children.Add(separator);
        }

        private Button CreateButton(IToolButton button)
        {
            // Recoloured by the hook as the button is turned on and off, with the label and the menu arrow
            SolidColorBrush ink = new(ToolButtonHook.Ink);

            TextBlock name = new() { Text = button.Name ?? string.Empty, FontSize = 11, Foreground = ink };
            StackPanel label = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 4) };
            label.Children.Add(name);
            if (button.Menu)
            {
                Control arrow = ToolIconView.Create(ToolIcons.Chevron, ink, 9);
                arrow.Margin = new Thickness(2, 1, 0, 0);
                arrow.VerticalAlignment = VerticalAlignment.Center;
                label.Children.Add(arrow);
            }

            StackPanel middle = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            if (button.Icon is ToolIcon icon) middle.Children.Add(ToolIconView.Create(icon, ink, 22));
            middle.Children.Add(label);

            Border bar = new()
            {
                Height = 2,
                CornerRadius = new CornerRadius(1),
                Background = new SolidColorBrush(ToolButtonHook.OnInk),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(10, 0, 10, 2),
                IsVisible = false
            };

            TextBlock count = new()
            {
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(28, 28, 28)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Border badge = new()
            {
                MinWidth = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(3, 0),
                Background = new SolidColorBrush(ToolButtonHook.OnInk),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 3, 6, 0),
                Child = count,
                IsVisible = false
            };
            Panel badgeHolder = new() { Children = { badge } };

            Grid content = new();
            content.Children.Add(middle);
            content.Children.Add(bar);
            content.Children.Add(badgeHolder);

            Button output = new()
            {
                Content = content,
                Padding = new Thickness(6, 0),
                Margin = new Thickness(1, 0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };
            output.Classes.Add("tool");
            if (button.ToolTip is not null) ToolTip.SetTip(output, button.ToolTip);

            button.SetButton(output);
            _ = new ToolButtonHook(button, ink, bar, badge, count);
            _buttons.Add((output, label, badgeHolder));

            return output;
        }
    }
}
