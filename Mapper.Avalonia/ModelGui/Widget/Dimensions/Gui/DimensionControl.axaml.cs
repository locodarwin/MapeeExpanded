using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using WorldEditor;

namespace Mapper.Gui
{
    public partial class DimensionControl : UserControl
    {
        public IDimensionWidget Dimensions { get; }

        private List<DimensionButtonPanel> _dimensions = new List<DimensionButtonPanel>();
        private DimensionButtonPanel? _extraDimensionButton = null;

        public DimensionControl(IDimensionWidget dimensions)
        {
            InitializeComponent();

            Dimensions = dimensions;
            Dimensions.DimensionUpdate += Dimension_DimensionUpdate;
            Dimensions.DimensionSelectionChanged += Dimension_DimensionSelectionChanged;

            SetDimensions();
        }

        private void SetDimensions()
        {
            DimensionContainer.ColumnDefinitions.Clear();
            DimensionContainer.Children.Clear();
            _dimensions.Clear();

            foreach (DimensionUI dimension in Dimensions.Dimensions)
            {
                DimensionButtonPanel dimensionButton = CreateDimensionControl(dimension);

                dimensionButton.PointerPressed += Dimension_MouseDown;
                if (dimension.Dimension == Dimensions.CurrentDimension)
                {
                    dimensionButton.Select();
                }
            }

            if (Dimensions.ExtraDimensions.Count < 1) return;

            _extraDimensionButton = CreateDimensionControl(new DimensionUI(new Dimension("", "custom dimensions"), AppImages.Load("/Resources/Image/Dimension/ExtraDimensions_32px.png")));
            if (IsCustomDimensionSelected()) _extraDimensionButton.Select();

            _extraDimensionButton.PointerPressed += ExtraDimension_MouseDown;
        }
        private bool IsCustomDimensionSelected() 
        {
            foreach (Dimension dimension in Dimensions.ExtraDimensions) 
            {
                if(dimension == Dimensions.CurrentDimension) return true;
            }

            return false;
        }

        private DimensionButtonPanel CreateDimensionControl(DimensionUI dimension)
        {
            DimensionButtonPanel button = new DimensionButtonPanel(dimension)
            {
                Margin = new Thickness(4, 0, 0, 0),
                IsEnabled = Dimensions.IsDimensionAllowed(dimension.Dimension)
            };

            DimensionContainer.ColumnDefinitions.Add(new ColumnDefinition()
            {
                Width = GridLength.Auto
            });

            Grid.SetColumn(button, DimensionContainer.ColumnDefinitions.Count - 1);
            DimensionContainer.Children.Add(button);
            _dimensions.Add(button);

            return button;
        }

        private void Dimension_MouseDown(object? sender, PointerPressedEventArgs e) 
        {
            if (sender is null || sender is not DimensionButtonPanel button) return;
            Dimensions.CurrentDimension = button.Dimension.Dimension;
        }

        // A flyout under the button rather than the WPF GUI's borderless window, which Wayland won't let an app place
        private void ExtraDimension_MouseDown(object? sender, PointerPressedEventArgs e)
        {
            if (_extraDimensionButton is null) return;

            Flyout flyout = new() { Placement = PlacementMode.BottomEdgeAlignedRight };
            CustomDimensionWindow window = new CustomDimensionWindow(Dimensions.ExtraDimensions, Dimensions.CurrentDimension);

            window.DimensionSelected += (s, ee) =>
            {
                flyout.Hide();
                Dimensions.CurrentDimension = window.SelectedDimension;
            };

            flyout.Content = window;
            flyout.ShowAt(_extraDimensionButton);
        }

        private void Dimension_DimensionUpdate(object? sender, EventArgs e) 
        {
            SetDimensions();
        }
        private void Dimension_DimensionSelectionChanged(object? sender, EventArgs e) 
        {
            foreach (DimensionButtonPanel button in _dimensions) 
            {
                if (button.Dimension.Dimension == Dimensions.CurrentDimension)
                {
                    button.Select();
                }
                else 
                {
                    button.Deselect();
                }
            }

            if (IsCustomDimensionSelected()) _extraDimensionButton?.Select();
        }
    }
}
