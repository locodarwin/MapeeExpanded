using Mapper.Gui.Model;
using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Mapper.Gui.Logic
{
    public class MeasureLengthTool : ToggleableTool, IPainter
    {
        public IScene Scene { get; set; }
        public IRenderInvoker RenderInvoker { get; set; }
        public CanvasControl OutputControl { get; set; }

        public Pen GuideLinePen { get; set; }
        public Pen DiagonalLinePen { get; set; }
        public Pen RectangularLinePen { get; set; }

        public Brush TextBackgroundBrush { get; set; }

        private bool _isDown = false;
        private XzPoint _firstPoint, _secondPoint = new(double.MinValue, double.MinValue);
        private XzPoint _currentMousePoint = new(double.MinValue, double.MinValue);

        public MeasureLengthTool(IScene scene, IRenderInvoker renderInvoker, CanvasControl outputControl)
        {
            Scene = scene;
            OutputControl = outputControl;
            RenderInvoker = renderInvoker;

            GuideLinePen = new Pen(Brushes.Gray, 1)
            {
                DashStyle = new DashStyle(new double[] { 8, 4 }, 0),
            };

            DiagonalLinePen = new Pen(Brushes.White, 3);

            RectangularLinePen = new Pen(new SolidColorBrush(Color.FromArgb(220, 200, 200, 200)), 3)
            {
                DashStyle = new DashStyle(new double[] { 2, 3 }, 0)
            };

            TextBackgroundBrush = new SolidColorBrush(Color.FromArgb(192, 24, 24, 24));

            MouseHook.MouseDown += MouseHook_MouseDown;
            MouseHook.MouseMove += MouseHook_MouseMove;
            MouseHook.MouseUp += MouseHook_MouseUp;
        }

        public void Paint(DrawingContext drawingContext)
        {
            if (!Enabled || !IsTurnedOn) return;

            if(!_isDown && _currentMousePoint.X != double.MinValue) DrawGuideLines(drawingContext);
            if (_secondPoint.X == double.MinValue || _firstPoint.Equals(_secondPoint)) return;

            XzPoint third = GetThirdPoint(), fourth = GetFourthPoint();

            Point firstPoint = Scene.XzToPointOnScreen(_firstPoint);
            Point secondPoint = Scene.XzToPointOnScreen(_secondPoint);
            Point thirdPoint = Scene.XzToPointOnScreen(third);
            Point fourthPoint = Scene.XzToPointOnScreen(fourth);

            drawingContext.DrawRectangle(null, RectangularLinePen, new Rect(thirdPoint, fourthPoint).Normalize());
            drawingContext.DrawLine(DiagonalLinePen, firstPoint, secondPoint);

            FormattedText horizontalText = CreateHorizontalText(third, fourth);
            Point horizontalPoint = CreateHorizontalPoint(thirdPoint, fourthPoint, horizontalText);
            DrawText(drawingContext, horizontalPoint, horizontalText);

            horizontalPoint = horizontalPoint.WithY(fourthPoint.Y + (thirdPoint.Y - horizontalPoint.Y) - horizontalText.Height);
            DrawText(drawingContext, horizontalPoint, horizontalText);

            FormattedText verticalText = CreateVerticalText(third, fourth);
            Point verticalPoint = CreateVerticalPoint(thirdPoint, fourthPoint, verticalText);
            DrawText(drawingContext, verticalPoint, verticalText);

            verticalPoint = verticalPoint.WithX(fourthPoint.X + (thirdPoint.X - verticalPoint.X) - verticalText.Width);
            DrawText(drawingContext, verticalPoint, verticalText);
        }

        private XzPoint GetThirdPoint()
        {
            return new XzPoint(Math.Min(_firstPoint.X, _secondPoint.X), Math.Max(_firstPoint.Z, _secondPoint.Z));
        }
        private XzPoint GetFourthPoint()
        {
            return new XzPoint(Math.Max(_firstPoint.X, _secondPoint.X), Math.Min(_firstPoint.Z, _secondPoint.Z));
        }

        private void DrawGuideLines(DrawingContext drawingContext) 
        {
            Point verticalPoint0 = Scene.XzToPointOnScreen(new XzPoint(_currentMousePoint.X, Scene.TopLeft.Z));
            Point verticalPoint1 = Scene.XzToPointOnScreen(new XzPoint(_currentMousePoint.X, Scene.BottomRight.Z));
            drawingContext.DrawLine(GuideLinePen, verticalPoint0, verticalPoint1);

            Point horizontalPoint0 = Scene.XzToPointOnScreen(new XzPoint(Scene.TopLeft.X, _currentMousePoint.Z));
            Point horizontalPoint1 = Scene.XzToPointOnScreen(new XzPoint(Scene.BottomRight.X, _currentMousePoint.Z));
            drawingContext.DrawLine(GuideLinePen, horizontalPoint0, horizontalPoint1);
        }

        private static FormattedText CreateHorizontalText(XzPoint thirdPoint, XzPoint fourthPoint)
        {
            return CreateText(((int)(fourthPoint.X - thirdPoint.X)).ToString(), FlowDirection.LeftToRight);
        }
        private static Point CreateHorizontalPoint(Point thirdPoint, Point fourthPoint, FormattedText text)
        {
            return new((fourthPoint.X + thirdPoint.X) / 2 - text.Width / 2, thirdPoint.Y + 4);
        }

        private static FormattedText CreateVerticalText(XzPoint thirdPoint, XzPoint fourthPoint)
        {
            return CreateText(((int)(thirdPoint.Z - fourthPoint.Z)).ToString(), FlowDirection.LeftToRight);
        }
        private static Point CreateVerticalPoint(Point thirdPoint, Point fourthPoint, FormattedText text)
        {
            return new(thirdPoint.X - text.Width - 9, (fourthPoint.Y + thirdPoint.Y) / 2 - text.Height / 2);
        }

        private static FormattedText CreateText(string value, FlowDirection flowDirection)
        {
            return new FormattedText(value, CultureInfo.CurrentCulture, flowDirection, TextFont.Bold, 20, Brushes.Yellow);
        }

        private void DrawText(DrawingContext drawingContext, Point point, FormattedText text)
        {
            DrawTextBackground(drawingContext, point, text);
            drawingContext.DrawText(text, point);
        }
        private void DrawTextBackground(DrawingContext drawingContext, Point point, FormattedText text)
        {
            drawingContext.DrawRectangle(TextBackgroundBrush, null, new Rect(point.X - 5, point.Y, text.Width + 10, text.Height));
        }

        private void MouseHook_MouseDown(object? sender, Point point)
        {
            if (!Enabled || !IsTurnedOn || !OutputControl.IsMouseOver) return;
            _isDown = true;

            Scene.OffsetEnabled = false;

            _firstPoint = Scene.PointOnScreenToXz(AbsolutePointToRelative(point));
            _firstPoint.CastToIntegers();

            _secondPoint = new XzPoint(double.MinValue, double.MinValue);

            RenderInvoker.Render();
        }
        private void MouseHook_MouseMove(object? sender, Point point)
        {
            if (!Enabled || !IsTurnedOn) return;
            
            _currentMousePoint = Scene.PointOnScreenToXz(AbsolutePointToRelative(point));
            _currentMousePoint.CastToIntegers();
            
            if (_isDown) _secondPoint = _currentMousePoint;

            RenderInvoker.Render();
        }
        private void MouseHook_MouseUp(object? sender, Point point)
        {
            _isDown = false;
            Scene.OffsetEnabled = true;

            RenderInvoker.Render();
        }

        // The hook already reports points on the canvas
        private static Point AbsolutePointToRelative(Point point)
        {
            return point;
        }
    }
}
