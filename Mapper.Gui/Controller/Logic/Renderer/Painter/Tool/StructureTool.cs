using Mapper.Gui.Controller;
using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Mapper.Gui.Logic
{
    // Marks the villages, strongholds, temples and other structures of the dimension on show, each with its name where
    // there's room, and outlines the ground each covers when zoomed in far enough to see it. Where they go is
    // StructureFrame's work, shared with the Avalonia GUI; this only draws it
    public class StructureTool : ToggleableTool, IPainter
    {
        public DrawingGroup? DrawingGroup { get; set; }

        public Scene Scene { get; }
        public WorldStructures Structures { get; }
        public CanvasControl Canvas { get; }
        public SceneMarkupView View { get; }

        private static readonly Typeface LabelFont = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Brush LabelBackground = Frozen(new SolidColorBrush(Color.FromArgb(200, 20, 20, 20)));
        private static readonly Pen Edge = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(20, 20, 20)), 1.5));
        private static readonly Dictionary<string, Brush> Fills = [];
        private static readonly Dictionary<string, Pen> Outlines = [];

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

            double pixelsPerDip = VisualTreeHelper.GetDpi(Canvas).PixelsPerDip;
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
                if (shape.Labelled) DrawLabel(drawingContext, shape, pixelsPerDip);
            }
        }

        private static void DrawLabel(DrawingContext drawingContext, StructureShape shape, double pixelsPerDip)
        {
            FormattedText text = new(shape.Structure.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFont, 12, Brushes.White, pixelsPerDip);
            double width = text.Width + 10, height = text.Height + 4;
            double x = shape.LabelOnLeft ? shape.X - StructureShape.MarkGap - width : shape.X + StructureShape.MarkGap;
            double top = shape.Y - height / 2;
            drawingContext.DrawRoundedRectangle(LabelBackground, null, new Rect(Math.Round(x), Math.Round(top), Math.Ceiling(width), Math.Ceiling(height)), 3, 3);
            drawingContext.DrawText(text, new Point(Math.Round(x + 5), Math.Round(top + 2)));
        }

        private static Brush FillOf(string color)
        {
            if (!Fills.TryGetValue(color, out Brush? brush)) Fills[color] = brush = Frozen(new SolidColorBrush(ColorOf(color)));
            return brush;
        }

        private static Pen OutlineOf(string color)
        {
            if (!Outlines.TryGetValue(color, out Pen? pen))
            {
                Color parsed = ColorOf(color);
                Outlines[color] = pen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(200, parsed.R, parsed.G, parsed.B)), 1.5) { DashStyle = new DashStyle([2, 1.5], 0) });
            }
            return pen;
        }

        private static Color ColorOf(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
