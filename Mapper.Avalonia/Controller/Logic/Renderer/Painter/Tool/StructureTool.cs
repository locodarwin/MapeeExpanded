using Mapper.Gui.Controller;
using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace Mapper.Gui.Logic
{
    // Marks the villages, strongholds, temples and other structures of the dimension on show, each with its name where
    // there's room, and outlines the ground each covers when zoomed in far enough to see it. Where they go is
    // StructureFrame's work, shared with the WPF GUI; this only draws it
    public class StructureTool : ToggleableTool, IPainter
    {
        public Scene Scene { get; }
        public WorldStructures Structures { get; }
        public CanvasControl Canvas { get; }
        public SceneMarkupView View { get; }

        private static readonly Typeface LabelFont = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        private static readonly IBrush LabelBackground = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20));
        private static readonly IPen Edge = new Pen(new SolidColorBrush(Color.FromRgb(20, 20, 20)), 1.5);
        private static readonly Dictionary<string, IBrush> Fills = [];
        private static readonly Dictionary<string, IPen> Outlines = [];

        public StructureTool(Scene scene, WorldStructures structures, CanvasControl canvas, IRenderInvoker renderer)
        {
            Scene = scene;
            Structures = structures;
            Canvas = canvas;
            View = new SceneMarkupView(scene);

            Scene.WorldChanged += (old, current) => Enabled = current is not null;
            Structures.Changed += () =>
            {
                if (IsTurnedOn) renderer.Render();
            };
            OnTurnedOn += isTurnedOn =>
            {
                Structures.Shown = isTurnedOn;
                if (isTurnedOn) Structures.Want();
            };

            Enabled = false;
        }

        public void Paint(DrawingContext drawingContext)
        {
            if (!Enabled || !IsTurnedOn || Scene.Domain.CurrentWorld is null) return;
            Structures.Want();

            StructureFrame frame = StructureFrame.Build(Structures.Current, View, Canvas.ActualWidth, Canvas.ActualHeight);
            foreach (StructureShape shape in frame.Shapes)
            {
                if (shape.Box is var (left, top, right, bottom)) drawingContext.DrawRectangle(null, OutlineOf(shape.Color), new Rect(left, top, right - left, bottom - top));
            }
            // The kinds that matter most last, on top
            for (int i = frame.Shapes.Count - 1; i >= 0; i--)
            {
                StructureShape shape = frame.Shapes[i];
                double half = StructureFrame.MarkSize / 2;
                drawingContext.DrawEllipse(FillOf(shape.Color), Edge, new Point(shape.X, shape.Y), half, half);
            }
            foreach (StructureShape shape in frame.Shapes)
            {
                if (shape.Labelled) DrawLabel(drawingContext, shape);
            }
        }

        private static void DrawLabel(DrawingContext drawingContext, StructureShape shape)
        {
            FormattedText text = new(shape.Structure.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFont, 12, Brushes.White);
            double width = text.Width + 10, height = text.Height + 4;
            double x = shape.LabelOnLeft ? shape.X - StructureShape.MarkGap - width : shape.X + StructureShape.MarkGap;
            double top = shape.Y - height / 2;
            drawingContext.DrawRectangle(LabelBackground, null, new Rect(Math.Round(x), Math.Round(top), Math.Ceiling(width), Math.Ceiling(height)), 3, 3);
            drawingContext.DrawText(text, new Point(Math.Round(x + 5), Math.Round(top + 2)));
        }

        private static IBrush FillOf(string color)
        {
            if (!Fills.TryGetValue(color, out IBrush? brush)) Fills[color] = brush = new SolidColorBrush(Color.Parse(color));
            return brush;
        }

        private static IPen OutlineOf(string color)
        {
            if (!Outlines.TryGetValue(color, out IPen? pen))
            {
                Color parsed = Color.Parse(color);
                Outlines[color] = pen = new Pen(new SolidColorBrush(Color.FromArgb(200, parsed.R, parsed.G, parsed.B)), 1.5) { DashStyle = new DashStyle([4, 3], 0) };
            }
            return pen;
        }
    }
}
