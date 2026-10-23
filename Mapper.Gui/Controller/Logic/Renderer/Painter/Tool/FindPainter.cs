using Mapper.Gui.Controller;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Mapper.Gui.Logic
{
    // Marks what the block finder found. Where the marks go is FindFrame's work, shared with the Avalonia GUI; this
    // only draws it
    public class FindPainter : IPainter
    {
        public DrawingGroup? DrawingGroup { get; set; }

        public FindTool Tool { get; }
        public CanvasControl Canvas { get; }

        // Dark edges keep a mark apart from the map and from a bigger mark under it
        private static readonly Pen Edge = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), 1));
        private static readonly Pen RingPen = Frozen(new Pen(Brushes.White, 2));
        private static readonly Pen RingShadow = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), 4));

        // By colour and opacity in steps of 1/20, so a frame of thousands of squares shares a few brushes
        private readonly Dictionary<(int, int), Brush> _brushes = [];

        public FindPainter(FindTool tool, CanvasControl canvas)
        {
            Tool = tool;
            Canvas = canvas;
        }

        public void Paint(DrawingContext drawingContext)
        {
            if (Tool.Results is not FindResults results || Tool.Scene.Domain.CurrentWorld is null) return;

            FindFrame frame = FindFrame.Build(results, Tool.View, Canvas.ActualWidth, Canvas.ActualHeight, Tool.Picked);
            foreach (FindMark mark in frame.Marks)
            {
                Rect rect = new(mark.X, mark.Y, mark.Size, mark.Size);
                drawingContext.DrawRectangle(Brush(mark.Target, mark.Opacity), mark.Size >= 4 ? Edge : null, rect);
            }

            if (frame.Ring is var (x, y, radius))
            {
                drawingContext.DrawEllipse(null, RingShadow, new Point(x, y), radius, radius);
                drawingContext.DrawEllipse(null, RingPen, new Point(x, y), radius, radius);
            }
        }

        private Brush Brush(int target, double opacity)
        {
            int index = target % FindColors.All.Count, step = (int)Math.Round(opacity * 20);
            if (_brushes.TryGetValue((index, step), out Brush? brush)) return brush;

            Color color = (Color)ColorConverter.ConvertFromString(FindColors.All[index]);
            return _brushes[(index, step)] = Frozen(new SolidColorBrush(Color.FromArgb((byte)(step * 255 / 20), color.R, color.G, color.B)));
        }

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
