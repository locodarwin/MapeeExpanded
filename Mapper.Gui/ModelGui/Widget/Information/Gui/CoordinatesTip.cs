using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Mapper.Gui
{
    // The coordinates of the block under the mouse, shown beside it once it rests over the map for a moment and hidden
    // as soon as it moves. It lies in the map's grid cell, so the canvas's mouse positions place it. A ContentControl
    // around the border, since the main window takes its widgets as controls
    public class CoordinatesTip : ContentControl
    {
        private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(300);
        private const double CursorGapX = 14;
        private const double CursorGapY = 18;

        private readonly IInformationWidget _information;
        private readonly Scene _scene;
        private readonly CanvasControl _canvas;
        private readonly TextBlock _text;
        private readonly DispatcherTimer _timer;

        private Point _position;
        private (int X, int Z) _block;
        // What the inspector found at _block, once it has answered
        private InspectedColumn? _column;

        public CoordinatesTip(IInformationWidget information, Scene scene)
        {
            _information = information;
            _scene = scene;
            _canvas = information.MainControl;

            _text = new TextBlock() { FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(224, 224, 224)) };
            Content = new Border()
            {
                Child = _text,
                Background = new SolidColorBrush(Color.FromArgb(208, 24, 24, 24)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(72, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2)
            };
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            IsHitTestVisible = false;
            Visibility = Visibility.Collapsed;

            _timer = new DispatcherTimer() { Interval = Delay };
            _timer.Tick += (sender, e) => Show();

            _canvas.MouseMove += Canvas_MouseMove;
            _canvas.MouseDown += (sender, e) => Hide();
            _canvas.MouseUp += (sender, e) => Restart();
            _canvas.MouseLeave += (sender, e) => Hide();
            _information.ColumnUpdate += Information_ColumnUpdate;

            // Zooming and keyboard panning move the map under a still mouse
            _scene.Map.ScaleBehaviour.OffsetChanged += (sender, e) => Dispatcher.BeginInvoke(Restart);
            _scene.Map.ScaleBehaviour.ZoomChanged += (sender, e) => Dispatcher.BeginInvoke(Restart);
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            _position = e.GetPosition(_canvas);
            Restart();
        }

        private void Information_ColumnUpdate(object? sender, CursorColumn column)
        {
            if ((column.X, column.Z) != _block) return;

            _column = column.Column;
            if (Visibility == Visibility.Visible) _text.Text = CursorCoordinates.Format(_block.X, _block.Z, _column);
        }

        // No label while a button is held, as when dragging the map
        private static bool IsButtonDown()
        {
            return Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed ||
                Mouse.MiddleButton == MouseButtonState.Pressed;
        }

        private void Restart()
        {
            Hide();
            if (IsButtonDown() || !_canvas.IsMouseOver) return;

            XzPoint point = _scene.Map.TransformPointOnScreenToXz(_position);
            (int X, int Z) block = ((int)Math.Floor(point.X), (int)Math.Floor(point.Z));
            if (block != _block) _column = null;
            _block = block;

            _timer.Start();
        }

        private void Show()
        {
            _timer.Stop();
            if (_scene.Domain.CurrentWorld is null || IsButtonDown() || !_canvas.IsMouseOver) return;

            _text.Text = CursorCoordinates.Format(_block.X, _block.Z, _column);
            // A collapsed element measures as nothing
            Visibility = Visibility.Hidden;
            Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            // Below and right of the pointer, or on the other side where that would leave the map
            double x = _position.X + CursorGapX;
            double y = _position.Y + CursorGapY;
            if (x + DesiredSize.Width > _canvas.ActualWidth) x = _position.X - DesiredSize.Width - 4;
            if (y + DesiredSize.Height > _canvas.ActualHeight) y = _position.Y - DesiredSize.Height - 4;

            Margin = new Thickness(Math.Max(0, x), Math.Max(0, y), 0, 0);
            Visibility = Visibility.Visible;
        }

        private void Hide()
        {
            _timer.Stop();
            Visibility = Visibility.Collapsed;
        }
    }
}
