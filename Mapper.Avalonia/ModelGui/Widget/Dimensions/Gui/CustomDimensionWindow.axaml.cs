using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WorldEditor;

namespace Mapper.Gui
{
    // The list of a world's custom dimensions. Shown in a flyout; the WPF GUI uses a borderless window
    public partial class CustomDimensionWindow : UserControl
    {
        public Dimension SelectedDimension { get; private set; }

        public event EventHandler? DimensionSelected;

        public CustomDimensionWindow(IReadOnlyList<Dimension> dimensions, Dimension selectedDimension)
        {
            InitializeComponent();

            SelectedDimension = selectedDimension;
            AddButtons(dimensions);
        }

        private void AddButtons(IReadOnlyList<Dimension> dimensions) 
        {
            for (int i = 0; i < dimensions.Count; i++) 
            {
                Dimension dimension = dimensions[i];

                Button button = new()
                {
                    Content = dimension.Namespace,
                    FontSize = 14,
                    Height = 25,
                    Padding = new Avalonia.Thickness(25, 0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Background = new SolidColorBrush(Color.FromRgb(32, 32, 32)),
                    Cursor = new Cursor(StandardCursorType.Hand)
                };
                button.Classes.Add("flat");
                ToolTip.SetTip(button, $"Go to {dimension.Name}");
                if (dimension == SelectedDimension) button.FontWeight = FontWeight.UltraBold;

                button.Click += (sender, e) =>
                {
                    SelectedDimension = dimension;
                    DimensionSelected?.Invoke(this, EventArgs.Empty);
                };

                ButtonContainer.Children.Add(button);
            }
        }
    }
}
