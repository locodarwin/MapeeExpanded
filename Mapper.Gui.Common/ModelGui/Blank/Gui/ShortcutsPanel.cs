using System.Collections.Generic;
#if AVALONIA
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Element = Avalonia.Controls.Control;
#else
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Element = System.Windows.UIElement;
#endif

namespace Mapper.Gui
{
    // The key list (F1 or ?), over the whole map. Any key or click closes it; the GUI's key handling sees to the keys
    public class ShortcutsPanel : UserControl
    {
        private static readonly SolidColorBrush Text = new(Color.FromRgb(221, 221, 221));
        private static readonly SolidColorBrush Detail = new(Color.FromRgb(170, 170, 170));
        private static readonly SolidColorBrush Heading = new(Color.FromRgb(255, 215, 0));
#if AVALONIA
        private static readonly FontWeight Bold = FontWeight.Bold;
#else
        private static readonly FontWeight Bold = FontWeights.Bold;
#endif

        public bool IsOpen { get; private set; }

        public ShortcutsPanel(IReadOnlyList<ShortcutHelpGroup> groups)
        {
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;

            WrapPanel columns = new() { Orientation = Orientation.Horizontal, MaxWidth = 900 };
            foreach (ShortcutHelpGroup group in groups) columns.Children.Add(Group(group));

            StackPanel card = new();
            card.Children.Add(new TextBlock() { Text = "Keyboard shortcuts", FontSize = 16, Foreground = Text, Margin = new Thickness(14, 10, 14, 2) });
            card.Children.Add(new TextBlock() { Text = "None of these work while typing in a box. Press any key or click to close.", FontSize = 11, Foreground = Detail, Margin = new Thickness(14, 0, 14, 4) });
            card.Children.Add(columns);

            // The map dimmed behind the card, which also takes the click that closes it
            Content = new Border()
            {
                Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
                Child = new Border()
                {
                    Background = new SolidColorBrush(Color.FromRgb(28, 28, 28)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(112, 112, 112)),
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(20),
                    Child = card
                }
            };

#if AVALONIA
            IsVisible = false;
            PointerPressed += (sender, e) =>
            {
                Close();
                e.Handled = true;
            };
#else
            Visibility = Visibility.Collapsed;
            PreviewMouseDown += (sender, e) =>
            {
                Close();
                e.Handled = true;
            };
#endif
        }

        private static Grid Group(ShortcutHelpGroup group)
        {
            Grid grid = new() { Margin = new Thickness(14, 8, 14, 10), VerticalAlignment = VerticalAlignment.Top };
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
            Grid.SetColumnSpan(title, 2);
            Add(title, 0);

            foreach (ShortcutHelp line in group.Lines)
            {
                grid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
                Add(new TextBlock() { Text = line.Keys, FontSize = 12, FontWeight = Bold, Foreground = Text, Margin = new Thickness(0, 1, 14, 1) }, 0);
                Add(new TextBlock() { Text = line.Description, FontSize = 12, Foreground = Detail, Margin = new Thickness(0, 1, 0, 1) }, 1);
            }
            return grid;
        }

        public void Open()
        {
            IsOpen = true;
#if AVALONIA
            IsVisible = true;
#else
            Visibility = Visibility.Visible;
#endif
        }

        public void Close()
        {
            IsOpen = false;
#if AVALONIA
            IsVisible = false;
#else
            Visibility = Visibility.Collapsed;
#endif
        }
    }
}
