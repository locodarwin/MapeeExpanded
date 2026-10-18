using Mapper.Gui.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Mapper.Gui
{
    // The WPF control measures each button to size itself; here the grid's Auto columns do that
    public partial class ToolbarControl : UserControl
    {
        public IToolbarWidget Toolbar { get; }

        public ToolbarControl(IToolbarWidget toolbar)
        {
            InitializeComponent();

            Toolbar = toolbar;
            LoadSegments();
        }

        private void LoadSegments()
        {
            for (int i = 0; i < Toolbar.ToolButtonSegments.Count; i++)
            {
                IToolButtonSegment segment = Toolbar.ToolButtonSegments[i];

                AddGap(segment.LeftGap);
                LoadSegment(segment);
                AddGap(segment.RightGap);
            }
        }
        private void LoadSegment(IToolButtonSegment segment) 
        {
            ToolButtonHook? prevHook = null;
            DockPanel? prevPanel = null;
            Border? prevSeparator = null;

            for (int i = 0; i < segment.Tools.Count; i++)
            {
                IToolButton button = segment.Tools[i];

                Button control = CreateButton(button);

                ButtonContainer.ColumnDefinitions.Add(new ColumnDefinition()
                {
                    Width = GridLength.Auto
                });

                Grid.SetRow(control, 0);
                Grid.SetColumn(control, ButtonContainer.ColumnDefinitions.Count - 1);
                ButtonContainer.Children.Add(control);

                ToolButtonHook hook = new ToolButtonHook(button);
                if (prevHook is not null && prevPanel is not null && prevSeparator is not null)
                {
                    _ = new SeparatorHook(prevPanel, prevSeparator, prevHook, hook);
                }

                if (i < segment.Tools.Count - 1)
                {
                    AddSeparator(out DockPanel panel, out Border separator);

                    prevPanel = panel;
                    prevSeparator = separator;
                }

                prevHook = hook;
            }
        }
        private void AddGap(double width)
        {
            if (width <= 0) return;

            ButtonContainer.ColumnDefinitions.Add(new ColumnDefinition()
            {
                Width = new GridLength(width, GridUnitType.Pixel)
            });
        }
        private void AddSeparator(out DockPanel panel, out Border separator) 
        {
            panel = CreateSeparator(out separator);

            ButtonContainer.ColumnDefinitions.Add(new ColumnDefinition()
            {
                Width = new GridLength(panel.Width, GridUnitType.Pixel)
            });

            Grid.SetRow(panel, 0);
            Grid.SetColumn(panel, ButtonContainer.ColumnDefinitions.Count - 1);
            ButtonContainer.Children.Add(panel);
        }

        private Button CreateButton(IToolButton button) 
        {
            Button output = new() 
            {
                Height = 33,
                Content = CreatePanel(button),
                VerticalContentAlignment = VerticalAlignment.Stretch
            };
            output.Classes.Add("tool");
            if (button.ToolTip is not null) ToolTip.SetTip(output, button.ToolTip);

            button.SetButton(output);

            return output;
        }
        private StackPanel CreatePanel(IToolButton button) 
        {
            StackPanel output = new()
            {
                Height = 33,
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 6, 0)
            };

            Image image = CreateImageControl(button);
            TextBlock block = CreateTextBlockControl(button);

            output.Children.Add(image);
            output.Children.Add(block);

            if (image.IsVisible && block.IsVisible) 
            {
                image.Margin = new Thickness(image.Margin.Left + 2, image.Margin.Top, image.Margin.Right, image.Margin.Bottom);
            }

            return output;
        }
        private static Image CreateImageControl(IToolButton button) 
        {
            Image output = new() 
            {
                Stretch = Stretch.None,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0)
            };

            if (button.Icon is not null)
            {
                output.Source = button.Icon;
            }
            else 
            {
                output.IsVisible = false;
            }

            return output;
        }
        private static TextBlock CreateTextBlockControl(IToolButton button)
        {
            TextBlock textBlock = new() 
            {
                Foreground = new SolidColorBrush(Color.FromRgb(239, 239, 239)),
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(7, 0, 3, 1)
            };

            if (button.Name is not null)
            {
                textBlock.Text = button.Name;
            }
            else
            {
                textBlock.IsVisible = false;
            }

            return textBlock;
        }

        private static DockPanel CreateSeparator(out Border border) 
        {
            DockPanel output = new DockPanel() 
            {
                Background = Brushes.Transparent,
                Width = 1
            };

            border = new Border()
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(112, 112, 112)),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Margin = new Thickness(0, 7, 0, 7)
            };

            output.Children.Add(border);

            return output;
        }
    }
}
