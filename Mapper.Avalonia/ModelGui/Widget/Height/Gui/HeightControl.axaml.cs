using Mapper.Gui.Model;
using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

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
            HeightSlider.AddHandler(PointerWheelChangedEvent, HeightSlider_PointerWheelChanged, RoutingStrategies.Tunnel);

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
            SurfaceButton.Classes.Set("selected", Widget.Mode == HeightMode.Surface);
            SliceButton.Classes.Set("selected", Widget.Mode == HeightMode.Slice);
            CavesButton.Classes.Set("selected", Widget.Mode == HeightMode.Caves);

            HeightSlider.IsEnabled = Widget.Mode != HeightMode.Surface;
            HeightLabel.Text = Widget.Mode == HeightMode.Surface ? "Top" : $"Y {Widget.Y}";
        }

        private void SetMode(HeightMode mode) => Widget.Mode = mode;
        private void SurfaceButton_Click(object? sender, RoutedEventArgs e) => SetMode(HeightMode.Surface);
        private void SliceButton_Click(object? sender, RoutedEventArgs e) => SetMode(HeightMode.Slice);
        private void CavesButton_Click(object? sender, RoutedEventArgs e) => SetMode(HeightMode.Caves);

        private void HeightSlider_ValueChanged(object? sender, EventArgs e)
        {
            if (_reading) return;

            Widget.Y = (int)Math.Round(HeightSlider.Value);
        }
        private void HeightSlider_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            e.Handled = true;
            if (!HeightSlider.IsEnabled) return;

            int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
            // Shift turns the wheel sideways on some systems
            double delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
            HeightSlider.Value = Math.Clamp(HeightSlider.Value + Math.Sign(delta) * step, HeightSlider.Minimum, HeightSlider.Maximum);
        }
    }
}
