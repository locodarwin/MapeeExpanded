using Mapper.Gui.Controller;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace Mapper.Gui.Logic
{
    // Draws the world's pins and zones over the map, and the image export's copy of them. What goes where is
    // MarkupFrame's work, shared with the WPF GUI; this only draws it
    public class MarkupPainter : IPainter
    {
        public MarkupTool Tool { get; }
        public CanvasControl Canvas { get; }

        // Set while a screenshot is taken without markup
        public bool Hidden { get; set; }

        private static readonly Typeface LabelFont = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        private static readonly Typeface DetailFont = new(FontFamily.Default, FontStyle.Normal, FontWeight.Normal);
        private static readonly IBrush LabelBackground = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20));
        private static readonly IBrush DetailBrush = new SolidColorBrush(Color.FromRgb(210, 210, 210));
        private static readonly IPen PinEdge = new Pen(new SolidColorBrush(Color.FromRgb(26, 26, 26)), 1.5);
        private static readonly IPen Ring = new Pen(Brushes.White, 2);
        private static readonly IPen DraftPen = new Pen(Brushes.White, 2) { DashStyle = new DashStyle([4, 3], 0) };
        private static readonly IPen DraftShadow = new Pen(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), 4);
        private static readonly IPen HandleEdge = new Pen(new SolidColorBrush(Color.FromRgb(26, 26, 26)), 1);

        private static readonly Dictionary<string, Geometry> Icons = PinIcons.All.ToDictionary(i => i.Id, i => Geometry.Parse("F0 " + i.Path));

        public MarkupPainter(MarkupTool tool, CanvasControl canvas)
        {
            Tool = tool;
            Canvas = canvas;
        }

        public void Paint(DrawingContext drawingContext)
        {
            if (Hidden || !Tool.Editor.Visible || Tool.Scene.Domain.CurrentWorld is null) return;

            MarkupFrame frame = MarkupFrame.Build(Tool.Editor.Document, Tool.View, Canvas.ActualWidth, Canvas.ActualHeight, Tool.Editor);
            Draw(drawingContext, frame);
        }

        public static void Draw(DrawingContext drawingContext, MarkupFrame frame)
        {
            foreach (ZoneShape zone in frame.Zones) DrawZone(drawingContext, zone);
            foreach (ZoneShape zone in frame.Zones) DrawZoneLabel(drawingContext, zone);
            foreach (PinShape pin in frame.Pins) DrawPin(drawingContext, pin);

            DrawHandles(drawingContext, frame);
            DrawDraft(drawingContext, frame);
            DrawRectangle(drawingContext, frame);
        }

        private static void DrawZone(DrawingContext drawingContext, ZoneShape zone)
        {
            Color color = ColorOf(zone.Zone.Color);
            byte alpha = zone.Hovered || zone.Selected ? (byte)90 : (byte)56;
            Pen edge = new(new SolidColorBrush(color), zone.Selected ? 3 : 2);

            drawingContext.DrawGeometry(new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)), edge, Polygon(zone.Points, true));
        }

        private static void DrawZoneLabel(DrawingContext drawingContext, ZoneShape zone)
        {
            string label = zone.Zone.Label;
            bool emphasised = zone.Hovered || zone.Selected;
            if (label.Length == 0 && !emphasised) return;

            FormattedText title = Text(label.Length > 0 ? label : "Unnamed zone", LabelFont, 13, Brushes.White);
            if (!emphasised && (title.Width + 8 > zone.ScreenWidth || title.Height + 4 > zone.ScreenHeight)) return;

            List<FormattedText> lines = [title];
            if (emphasised && zone.Detail is string detail) lines.Add(Text(detail, DetailFont, 11, DetailBrush));
            if (emphasised && NoteLine(zone.Zone.Note) is string note) lines.Add(Text(note, DetailFont, 11, DetailBrush));

            DrawLines(drawingContext, lines, zone.LabelAt.X, zone.LabelAt.Y, true);
        }

        private static void DrawPin(DrawingContext drawingContext, PinShape pin)
        {
            Point centre = new(pin.X, pin.Y);
            Color color = ColorOf(pin.Pin.Color);
            double radius = MarkupEditor.PinRadius;

            if (pin.Hovered || pin.Selected) drawingContext.DrawEllipse(null, Ring, centre, radius + 3, radius + 3);
            drawingContext.DrawEllipse(new SolidColorBrush(color), PinEdge, centre, radius, radius);

            IBrush glyph = MarkupColors.IsLight(pin.Pin.Color) ? new SolidColorBrush(Color.FromRgb(32, 32, 32)) : Brushes.White;
            if (pin.Pin.Icon is string icon && Icons.TryGetValue(icon, out Geometry? geometry))
            {
                using (drawingContext.PushTransform(Matrix.CreateScale(0.75, 0.75) * Matrix.CreateTranslation(pin.X - 6, pin.Y - 6)))
                {
                    drawingContext.DrawGeometry(glyph, null, geometry);
                }
            }
            else
            {
                drawingContext.DrawEllipse(glyph, null, centre, 2.5, 2.5);
            }

            List<FormattedText> lines = [];
            if (pin.Pin.Label.Length > 0) lines.Add(Text(pin.Pin.Label, LabelFont, 12, Brushes.White));
            if (pin.Hovered || pin.Selected)
            {
                if (lines.Count == 0) lines.Add(Text("Unnamed pin", LabelFont, 12, Brushes.White));
                lines.Add(Text($"{pin.Pin.X}; {pin.Pin.Z}", DetailFont, 11, DetailBrush));
                if (NoteLine(pin.Pin.Note) is string note) lines.Add(Text(note, DetailFont, 11, DetailBrush));
            }
            if (lines.Count > 0) DrawLines(drawingContext, lines, pin.X + radius + 4, pin.Y, false);
        }

        private static void DrawHandles(DrawingContext drawingContext, MarkupFrame frame)
        {
            for (int i = 0; i < frame.Handles.Count; i++)
            {
                double size = i == frame.HoveredHandle ? 6 : 4;
                var (x, y) = frame.Handles[i];
                drawingContext.DrawRectangle(i == frame.HoveredHandle ? Brushes.Gold : Brushes.White, HandleEdge, new Rect(x - size, y - size, size * 2, size * 2));
            }
        }

        private static void DrawDraft(DrawingContext drawingContext, MarkupFrame frame)
        {
            if (frame.Draft.Count == 0) return;

            List<(double X, double Y)> points = [.. frame.Draft];
            if (frame.DraftCursor is { } cursor) points.Add(cursor);

            if (points.Count > 1)
            {
                Geometry line = Polygon([.. points], false);
                drawingContext.DrawGeometry(null, DraftShadow, line);
                drawingContext.DrawGeometry(null, DraftPen, line);
            }

            for (int i = 0; i < frame.Draft.Count; i++)
            {
                var (x, y) = frame.Draft[i];
                double radius = i == 0 && frame.DraftClosable ? 6 : 3.5;
                drawingContext.DrawEllipse(i == 0 && frame.DraftClosable ? Brushes.Gold : Brushes.White, HandleEdge, new Point(x, y), radius, radius);
            }
        }

        private static void DrawRectangle(DrawingContext drawingContext, MarkupFrame frame)
        {
            if (frame.Rectangle is not { } bounds) return;
            var (left, top, right, bottom) = bounds;

            Rect rect = new(new Point(left, top), new Point(right, bottom));
            drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), DraftShadow, rect);
            drawingContext.DrawRectangle(null, DraftPen, rect);
            if (frame.RectangleText is string text) DrawLines(drawingContext, [Text(text, DetailFont, 12, Brushes.White)], right + 6, bottom, false);
        }

        // A block of lines on a dark backing, centred on the point or starting just right of it
        private static void DrawLines(DrawingContext drawingContext, List<FormattedText> lines, double x, double y, bool centred)
        {
            double width = lines.Max(l => l.Width) + 10, height = lines.Sum(l => l.Height) + 4;
            double left = centred ? x - width / 2 : x, top = y - height / 2;

            drawingContext.DrawRectangle(LabelBackground, null, new Rect(Math.Round(left), Math.Round(top), Math.Ceiling(width), Math.Ceiling(height)), 3, 3);

            double lineTop = top + 2;
            foreach (FormattedText line in lines)
            {
                double lineLeft = centred ? x - line.Width / 2 : left + 5;
                drawingContext.DrawText(line, new Point(Math.Round(lineLeft), Math.Round(lineTop)));
                lineTop += line.Height;
            }
        }

        private static string? NoteLine(string? note)
        {
            if (string.IsNullOrWhiteSpace(note)) return null;
            string line = note.Trim().Split('\n')[0].Trim();
            return line.Length > 60 ? line[..57] + "..." : line;
        }

        private static Geometry Polygon((double X, double Y)[] points, bool closed)
        {
            StreamGeometry geometry = new();
            using StreamGeometryContext context = geometry.Open();
            context.BeginFigure(new Point(points[0].X, points[0].Y), closed);
            for (int i = 1; i < points.Length; i++) context.LineTo(new Point(points[i].X, points[i].Y));
            context.EndFigure(closed);
            return geometry;
        }

        private static FormattedText Text(string text, Typeface typeface, double size, IBrush brush)
        {
            return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush);
        }

        private static Color ColorOf(string hex)
        {
            var (r, g, b) = MarkupColors.Parse(hex);
            return Color.FromRgb(r, g, b);
        }
    }
}
