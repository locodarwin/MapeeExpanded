using Mapper.Gui.Controller;
using Mapper.Gui.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using WorldEditor;

namespace Mapper.Gui.Logic
{
    // Marks the Nether portals in the Overworld and the Nether, each labelled with where it leads, a dashed line to
    // where that portal stands, and in red the ones whose partner leads somewhere else. Where they go is
    // PortalFrame's work, shared with the Avalonia GUI; this only draws it
    public class PortalTool : ToggleableTool, IPainter
    {
        public DrawingGroup? DrawingGroup { get; set; }

        public Scene Scene { get; }
        public NetherPortals Portals { get; }
        public CanvasControl Canvas { get; }
        public SceneMarkupView View { get; }

        private static readonly Typeface LabelFont = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Typeface DetailFont = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Brush LabelBackground = Frozen(new SolidColorBrush(Color.FromArgb(200, 20, 20, 20)));
        private static readonly Brush DetailBrush = Frozen(new SolidColorBrush(Color.FromRgb(255, 190, 190)));
        private static readonly Pen Edge = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(20, 20, 20)), 1.5));
        private static readonly Pen Leader = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(200, 230, 230, 230)), 1));
        private static readonly Pen LineShadow = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), 4));
        private static readonly Dictionary<PortalState, Brush> Fills = PortalFrame.Colors.ToDictionary(c => c.Key, c => (Brush)Frozen(new SolidColorBrush(ColorOf(c.Value))));
        private static readonly Dictionary<PortalState, Pen> Lines = PortalFrame.Colors.ToDictionary(c => c.Key, c => Frozen(new Pen(new SolidColorBrush(ColorOf(c.Value)), 2) { DashStyle = new DashStyle([2, 1.5], 0) }));

        public PortalTool(Scene scene, NetherPortals portals, CanvasControl canvas, IRenderInvoker renderer)
        {
            Scene = scene;
            Portals = portals;
            Canvas = canvas;
            View = new SceneMarkupView(scene);

            Scene.WorldChanged += (old, current) => Enabled = HasPortals();
            Scene.DimensionChanged += (old, current) => Enabled = HasPortals();
            Portals.Changed += () =>
            {
                if (IsTurnedOn) renderer.Render();
            };
            OnTurnedOn += isTurnedOn =>
            {
                if (isTurnedOn) Portals.Want();
            };

            Enabled = false;
        }

        private bool HasPortals() => Scene.Domain.CurrentWorld?.CurrentDimension.Dimension is Dimension dimension && (dimension == Dimension.Overworld || dimension == Dimension.Nether);

        public void Paint(DrawingContext drawingContext)
        {
            if (!Enabled || !IsTurnedOn || Scene.Domain.CurrentWorld is not WorldDomain world) return;
            Portals.Want();

            double pixelsPerDip = VisualTreeHelper.GetDpi(Canvas).PixelsPerDip;
            PortalFrame frame = PortalFrame.Build(Portals.Map, world.CurrentDimension.Dimension, View, Canvas.ActualWidth, Canvas.ActualHeight);
            foreach (PortalShape shape in frame.Portals)
            {
                if (shape.Partner is var (x, y))
                {
                    drawingContext.DrawLine(LineShadow, new Point(shape.X, shape.Y), new Point(x, y));
                    drawingContext.DrawLine(Lines[shape.State], new Point(shape.X, shape.Y), new Point(x, y));
                }
            }
            foreach (PortalShape shape in frame.Portals)
            {
                double half = shape.Size / 2;
                drawingContext.DrawRoundedRectangle(Fills[shape.State], Edge, new Rect(shape.X - half, shape.Y - half, shape.Size, shape.Size), 2, 2);
                // A label moved out of another's way keeps a line to its portal
                if (Math.Abs(shape.LabelY - shape.Y) > 1) drawingContext.DrawLine(Leader, new Point(shape.X + half, shape.Y), new Point(shape.LabelX, shape.LabelY));
                DrawLabel(drawingContext, shape, shape.LabelX, shape.LabelY, pixelsPerDip);
            }
        }

        private static void DrawLabel(DrawingContext drawingContext, PortalShape shape, double x, double y, double pixelsPerDip)
        {
            List<FormattedText> lines = [Text(shape.Label, LabelFont, 12, Brushes.White, pixelsPerDip)];
            if (shape.Detail is string detail) lines.Add(Text(detail, DetailFont, 11, DetailBrush, pixelsPerDip));

            double width = lines.Max(l => l.Width) + 10, height = lines.Sum(l => l.Height) + 4;
            double top = y - height / 2;
            drawingContext.DrawRoundedRectangle(LabelBackground, null, new Rect(Math.Round(x), Math.Round(top), Math.Ceiling(width), Math.Ceiling(height)), 3, 3);

            double lineTop = top + 2;
            foreach (FormattedText line in lines)
            {
                drawingContext.DrawText(line, new Point(Math.Round(x + 5), Math.Round(lineTop)));
                lineTop += line.Height;
            }
        }

        private static FormattedText Text(string text, Typeface typeface, double size, Brush brush, double pixelsPerDip)
        {
            return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush, pixelsPerDip);
        }

        private static Color ColorOf(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
