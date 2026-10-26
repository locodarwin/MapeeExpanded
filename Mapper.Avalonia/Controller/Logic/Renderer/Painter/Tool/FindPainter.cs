using Mapper.Gui.Controller;
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace Mapper.Gui.Logic
{
    // Marks what the block finder found, and shades the biome patches the Biomes list names. Where they go is
    // FindFrame's and BiomeFrame's work, shared with the WPF GUI; this only draws it
    public class FindPainter : IPainter
    {
        public FindTool Tool { get; }
        public CanvasControl Canvas { get; }

        // Dark edges keep a mark apart from the map and from a bigger mark under it
        private static readonly IPen Edge = new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), 1);
        private static readonly IPen RingPen = new Pen(Brushes.White, 2);
        private static readonly IPen RingShadow = new Pen(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), 4);

        // By colour and opacity in steps of 1/20, so a frame of thousands of squares shares a few brushes
        private readonly Dictionary<(int, int), IBrush> _brushes = [];

        public FindPainter(FindTool tool, CanvasControl canvas)
        {
            Tool = tool;
            Canvas = canvas;
        }

        private static readonly IBrush BiomeShade = Shade(BiomeFrame.ShadedOpacity), BiomePicked = Shade(BiomeFrame.PickedOpacity);

        public void Paint(DrawingContext drawingContext)
        {
            if (Tool.Scene.Domain.CurrentWorld is null) return;

            if (Tool.Biomes is { Listed: true } biomes && (biomes.Shaded.Count > 0 || biomes.Picked is not null))
            {
                foreach (BiomeMark mark in BiomeFrame.Build(biomes.Shaded, biomes.Picked, Tool.View, Canvas.ActualWidth, Canvas.ActualHeight))
                {
                    drawingContext.DrawRectangle(mark.Picked ? BiomePicked : BiomeShade, null, new Rect(mark.X, mark.Y, mark.Width, mark.Height));
                }
            }

            if (Tool.Results is not FindResults results) return;

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

        private static IBrush Shade(double opacity)
        {
            Color color = Color.Parse(BiomeFrame.Color);
            return new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B));
        }

        private IBrush Brush(int target, double opacity)
        {
            int index = target % FindColors.All.Count, step = (int)Math.Round(opacity * 20);
            if (_brushes.TryGetValue((index, step), out IBrush? brush)) return brush;

            Color color = Color.Parse(FindColors.All[index]);
            return _brushes[(index, step)] = new SolidColorBrush(Color.FromArgb((byte)(step * 255 / 20), color.R, color.G, color.B));
        }
    }
}
