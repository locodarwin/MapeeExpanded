using Mapper.Gui.Model;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Mapper.Gui
{
    public partial class HeightControl : UserControl
    {
        public IHeightWidget Widget { get; }

        // Set while the controls are being filled in from the widget, so they don't write straight back
        private bool _reading;

        public HeightControl(IHeightWidget height)
        {
            InitializeComponent();

            Widget = height;
            Widget.Changed += (sender, e) => Read();

            HeightSlider.ValueChanged += HeightSlider_ValueChanged;
            HeightSlider.PreviewMouseWheel += HeightSlider_PreviewMouseWheel;

            Read();
        }

        private void Read()
        {
            _reading = true;

            IsEnabled = Widget.Enabled;
            HeightSlider.Minimum = Widget.MinY;
            HeightSlider.Maximum = Widget.MaxY;
            HeightSlider.Value = Widget.Y;
            ShowMode();

            _reading = false;
        }

        private void ShowMode()
        {
            SurfaceButton.Tag = Widget.Mode == HeightMode.Surface ? "selected" : null;
            SliceButton.Tag = Widget.Mode == HeightMode.Slice ? "selected" : null;
            CavesButton.Tag = Widget.Mode == HeightMode.Caves ? "selected" : null;

            HeightSlider.IsEnabled = Widget.Mode != HeightMode.Surface;
            HeightLabel.Content = Widget.Mode == HeightMode.Surface ? "Top" : $"Y {Widget.Y}";
        }

        private void SetMode(HeightMode mode) => Widget.Mode = mode;
        private void SurfaceButton_Click(object sender, RoutedEventArgs e) => SetMode(HeightMode.Surface);
        private void SliceButton_Click(object sender, RoutedEventArgs e) => SetMode(HeightMode.Slice);
        private void CavesButton_Click(object sender, RoutedEventArgs e) => SetMode(HeightMode.Caves);

        private void HeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_reading) return;

            Widget.Y = (int)Math.Round(HeightSlider.Value);
        }
        private void HeightSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            if (!HeightSlider.IsEnabled) return;

            int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            HeightSlider.Value = Math.Clamp(HeightSlider.Value + Math.Sign(e.Delta) * step, HeightSlider.Minimum, HeightSlider.Maximum);
        }
    }
}
