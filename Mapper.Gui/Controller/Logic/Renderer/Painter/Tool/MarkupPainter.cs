using Mapper.Gui.Controller;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Mapper.Gui.Logic
{
    // Draws the world's pins, zones and shown signs over the map, and the image export's copy of them. What goes where is
    // MarkupFrame's work, shared with the Avalonia GUI; this only draws it
    public class MarkupPainter : IPainter
    {
        public DrawingGroup? DrawingGroup { get; set; }

        public MarkupTool Tool { get; }
        public CanvasControl Canvas { get; }

        // Set while a screenshot is taken without markup
        public bool Hidden { get; set; }

        private static readonly Typeface LabelFont = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Typeface DetailFont = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Brush LabelBackground = Frozen(new SolidColorBrush(Color.FromArgb(200, 20, 20, 20)));
        private static readonly Brush DetailBrush = Frozen(new SolidColorBrush(Color.FromRgb(210, 210, 210)));
        private static readonly Brush DarkGlyph = Frozen(new SolidColorBrush(Color.FromRgb(32, 32, 32)));
        private static readonly Brush RectangleFill = Frozen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)));
        private static readonly Pen PinEdge = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(26, 26, 26)), 1.5));
        private static readonly Pen Ring = Frozen(new Pen(Brushes.White, 2));
        private static readonly Brush Wood = Frozen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(SignShape.Wood)));
        private static readonly Pen DraftPen = Frozen(new Pen(Brushes.White, 2) { DashStyle = new DashStyle([2, 1.5], 0) });
        private static readonly Pen DraftShadow = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), 4));
        private static readonly Pen HandleEdge = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(26, 26, 26)), 1));

        private static readonly Dictionary<string, Geometry> Icons = PinIcons.All.ToDictionary(i => i.Id, i => Frozen(Geometry.Parse("F0 " + i.Path)));

        public MarkupPainter(MarkupTool tool, CanvasControl canvas)
        {
            Tool = tool;
            Canvas = canvas;
        }

        public void Paint(DrawingContext drawingContext)
        {
            if (Hidden || !Tool.Editor.Visible || Tool.Scene.Domain.CurrentWorld is null) return;

            MarkupFrame frame = MarkupFrame.Build(Tool.Editor.Document, Tool.View, Canvas.ActualWidth, Canvas.ActualHeight, Tool.Editor);
            Draw(drawingContext, frame, VisualTreeHelper.GetDpi(Canvas).PixelsPerDip);
        }

        public static void Draw(DrawingContext drawingContext, MarkupFrame frame, double pixelsPerDip)
        {
            foreach (ZoneShape zone in frame.Zones) DrawZone(drawingContext, zone);
            foreach (ZoneShape zone in frame.Zones) DrawZoneLabel(drawingContext, zone, pixelsPerDip);
            foreach (SignShape sign in frame.Signs) DrawSign(drawingContext, sign, pixelsPerDip);
            foreach (PinShape pin in frame.Pins) DrawPin(drawingContext, pin, pixelsPerDip);

            DrawHandles(drawingContext, frame);
            DrawDraft(drawingContext, frame);
            DrawRectangle(drawingContext, frame, pixelsPerDip);
        }

        private static void DrawZone(DrawingContext drawingContext, ZoneShape zone)
        {
            Color color = ColorOf(zone.Zone.Color);
            byte alpha = zone.Hovered || zone.Selected ? (byte)90 : (byte)56;
            Pen edge = new(new SolidColorBrush(color), zone.Selected ? 3 : 2);

            drawingContext.DrawGeometry(new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)), edge, Polygon(zone.Points, true));
        }

        private static void DrawZoneLabel(DrawingContext drawingContext, ZoneShape zone, double pixelsPerDip)
        {
            string label = zone.Zone.Label;
            bool emphasised = zone.Hovered || zone.Selected;
            if (label.Length == 0 && !emphasised) return;

            FormattedText title = Text(label.Length > 0 ? label : "Unnamed zone", LabelFont, 13, Brushes.White, pixelsPerDip);
            if (!emphasised && (title.Width + 8 > zone.ScreenWidth || title.Height + 4 > zone.ScreenHeight)) return;

            List<FormattedText> lines = [title];
            if (emphasised && zone.Detail is string detail) lines.Add(Text(detail, DetailFont, 11, DetailBrush, pixelsPerDip));
            if (emphasised && NoteLine(zone.Zone.Note) is string note) lines.Add(Text(note, DetailFont, 11, DetailBrush, pixelsPerDip));

            DrawLines(drawingContext, lines, zone.LabelAt.X, zone.LabelAt.Y, true);
        }

        private static void DrawPin(DrawingContext drawingContext, PinShape pin, double pixelsPerDip)
        {
            Point centre = new(pin.X, pin.Y);
            Color color = ColorOf(pin.Pin.Color);
            double radius = MarkupEditor.PinRadius;

            if (pin.Hovered || pin.Selected) drawingContext.DrawEllipse(null, Ring, centre, radius + 3, radius + 3);
            drawingContext.DrawEllipse(new SolidColorBrush(color), PinEdge, centre, radius, radius);

            Brush glyph = MarkupColors.IsLight(pin.Pin.Color) ? DarkGlyph : Brushes.White;
            if (pin.Pin.Icon is string icon && Icons.TryGetValue(icon, out Geometry? geometry))
            {
                TransformGroup transform = new();
                transform.Children.Add(new ScaleTransform(0.75, 0.75));
                transform.Children.Add(new TranslateTransform(pin.X - 6, pin.Y - 6));
                drawingContext.PushTransform(transform);
                drawingContext.DrawGeometry(glyph, null, geometry);
                drawingContext.Pop();
            }
            else
            {
                drawingContext.DrawEllipse(glyph, null, centre, 2.5, 2.5);
            }

            List<FormattedText> lines = [];
            if (pin.Pin.Label.Length > 0) lines.Add(Text(pin.Pin.Label, LabelFont, 12, Brushes.White, pixelsPerDip));
            if (pin.Hovered || pin.Selected)
            {
                if (lines.Count == 0) lines.Add(Text("Unnamed pin", LabelFont, 12, Brushes.White, pixelsPerDip));
                lines.Add(Text($"{pin.Pin.X}; {pin.Pin.Z}", DetailFont, 11, DetailBrush, pixelsPerDip));
                if (NoteLine(pin.Pin.Note) is string note) lines.Add(Text(note, DetailFont, 11, DetailBrush, pixelsPerDip));
            }
            if (lines.Count > 0) DrawLines(drawingContext, lines, pin.X + radius + 4, pin.Y, false);
        }

        // A small sign standing on its block, with its first line beside it, or every line while the mouse is over it
        private static void DrawSign(DrawingContext drawingContext, SignShape sign, double pixelsPerDip)
        {
            var (postLeft, postTop, postWidth, postHeight) = sign.Post;
            var (left, top, width, height) = sign.Board;
            drawingContext.DrawRectangle(PinEdge.Brush, null, new Rect(postLeft, postTop, postWidth, postHeight));
            drawingContext.DrawRoundedRectangle(Wood, sign.Hovered ? Ring : PinEdge, new Rect(left, top, width, height), 1.5, 1.5);

            List<FormattedText> lines = [];
            if (sign.Hovered)
            {
                IReadOnlyList<string> text = sign.Sign.Lines;
                lines.Add(Text(text[0], LabelFont, 12, Brushes.White, pixelsPerDip));
                lines.AddRange(text.Skip(1).Select(l => Text(l, DetailFont, 12, Brushes.White, pixelsPerDip)));
                lines.Add(Text($"{sign.Sign.X}, {sign.Sign.Y}, {sign.Sign.Z}", DetailFont, 11, DetailBrush, pixelsPerDip));
            }
            else if (sign.Labelled)
            {
                lines.Add(Text(sign.Sign.Title, LabelFont, 12, Brushes.White, pixelsPerDip));
            }
            if (lines.Count == 0) return;
            // While the mouse is over it, above it, clear of the coordinates beside the mouse
            double y = sign.Hovered ? sign.Y - SignShape.BoardHeight / 2 - (lines.Sum(l => l.Height) + 4) / 2 : sign.Y;
            DrawLines(drawingContext, lines, sign.X + MarkupEditor.SignRadius + 4, y, false);
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

        private static void DrawRectangle(DrawingContext drawingContext, MarkupFrame frame, double pixelsPerDip)
        {
            if (frame.Rectangle is not { } bounds) return;
            var (left, top, right, bottom) = bounds;

            Rect rect = new(new Point(left, top), new Point(right, bottom));
            drawingContext.DrawRectangle(RectangleFill, DraftShadow, rect);
            drawingContext.DrawRectangle(null, DraftPen, rect);
            if (frame.RectangleText is string text) DrawLines(drawingContext, [Text(text, DetailFont, 12, Brushes.White, pixelsPerDip)], right + 6, bottom, false);
        }

        // A block of lines on a dark backing, centred on the point or starting just right of it
        private static void DrawLines(DrawingContext drawingContext, List<FormattedText> lines, double x, double y, bool centred)
        {
            double width = lines.Max(l => l.Width) + 10, height = lines.Sum(l => l.Height) + 4;
            double left = centred ? x - width / 2 : x, top = y - height / 2;

            drawingContext.DrawRoundedRectangle(LabelBackground, null, new Rect(Math.Round(left), Math.Round(top), Math.Ceiling(width), Math.Ceiling(height)), 3, 3);

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
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(new Point(points[0].X, points[0].Y), closed, closed);
                for (int i = 1; i < points.Length; i++) context.LineTo(new Point(points[i].X, points[i].Y), true, false);
            }
            geometry.Freeze();
            return geometry;
        }

        private static FormattedText Text(string text, Typeface typeface, double size, Brush brush, double pixelsPerDip)
        {
            return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush, pixelsPerDip);
        }

        private static Color ColorOf(string hex)
        {
            var (r, g, b) = MarkupColors.Parse(hex);
            return Color.FromRgb(r, g, b);
        }

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
