using Mapper.Gui.Logic;
using Mapper.Gui.Model;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Mapper.Gui
{
    // The coordinates of the block under the mouse, shown beside it once it rests over the map for a moment and hidden
    // as soon as it moves. It lies in the map's grid cell, so the canvas's pointer positions place it
    public class CoordinatesTip : Border
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
        private bool _buttonDown;
        private (int X, int Z) _block;
        // What the inspector found at _block, once it has answered
        private InspectedColumn? _column;

        public CoordinatesTip(IInformationWidget information, Scene scene)
        {
            _information = information;
            _scene = scene;
            _canvas = information.MainControl;

            _text = new TextBlock() { FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(224, 224, 224)) };
            Child = _text;
            Background = new SolidColorBrush(Color.FromArgb(208, 24, 24, 24));
            BorderBrush = new SolidColorBrush(Color.FromArgb(72, 255, 255, 255));
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(3);
            Padding = new Thickness(6, 2);
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            IsHitTestVisible = false;
            IsVisible = false;

            _timer = new DispatcherTimer() { Interval = Delay };
            _timer.Tick += (sender, e) => Show();

            _canvas.PointerMoved += Canvas_PointerMoved;
            // No label while a button is held, as when dragging the map
            _canvas.PointerPressed += (sender, e) => { _buttonDown = true; Hide(); };
            _canvas.PointerReleased += (sender, e) => { _buttonDown = false; Restart(); };
            _canvas.PointerExited += (sender, e) => Hide();
            _information.ColumnUpdate += Information_ColumnUpdate;

            // Zooming and keyboard panning move the map under a still mouse
            _scene.Map.ScaleBehaviour.OffsetChanged += (sender, e) => Dispatcher.UIThread.Post(Restart);
            _scene.Map.ScaleBehaviour.ZoomChanged += (sender, e) => Dispatcher.UIThread.Post(Restart);
        }

        private void Canvas_PointerMoved(object? sender, PointerEventArgs e)
        {
            _position = e.GetPosition(_canvas);
            Restart();
        }

        private void Information_ColumnUpdate(object? sender, CursorColumn column)
        {
            if ((column.X, column.Z) != _block) return;

            _column = column.Column;
            if (IsVisible) _text.Text = CursorCoordinates.Format(_block.X, _block.Z, _column);
        }

        private void Restart()
        {
            Hide();
            if (_buttonDown || !_canvas.IsMouseOver) return;

            XzPoint point = _scene.Map.TransformPointOnScreenToXz(_position);
            (int X, int Z) block = ((int)Math.Floor(point.X), (int)Math.Floor(point.Z));
            if (block != _block) _column = null;
            _block = block;

            _timer.Start();
        }

        private void Show()
        {
            _timer.Stop();
            if (_scene.Domain.CurrentWorld is null || !_canvas.IsMouseOver) return;

            _text.Text = CursorCoordinates.Format(_block.X, _block.Z, _column);

            // A hidden control measures as nothing, so the size comes from the text
            _text.Measure(Size.Infinity);
            double width = _text.DesiredSize.Width + Padding.Left + Padding.Right + BorderThickness.Left + BorderThickness.Right;
            double height = _text.DesiredSize.Height + Padding.Top + Padding.Bottom + BorderThickness.Top + BorderThickness.Bottom;

            // Below and right of the pointer, or on the other side where that would leave the map
            double x = _position.X + CursorGapX;
            double y = _position.Y + CursorGapY;
            if (x + width > _canvas.ActualWidth) x = _position.X - width - 4;
            if (y + height > _canvas.ActualHeight) y = _position.Y - height - 4;

            Margin = new Thickness(Math.Max(0, x), Math.Max(0, y), 0, 0);
            IsVisible = true;
        }

        private void Hide()
        {
            _timer.Stop();
            IsVisible = false;
        }
    }
}
