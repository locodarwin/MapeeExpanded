using Mapper;
using Mapper.Gui.Controller;
using SkiaSharp;

namespace Mapee.Cli
{
    // Draws a world's pins and zones over an exported PNG, a band of rows at a time, the way the app's painters draw
    // them: zones filled and outlined with their labels where they fit, pins with their symbols and labels. Each band
    // draws everything that reaches into it, clipped to it, so shapes and labels run on across bands unbroken
    public class MarkupImage
    {
        private readonly MarkupDocument _document;
        private readonly string _dimension;

        private static readonly SKTypeface LabelFace = SKTypeface.FromFamilyName(null, SKFontStyle.Bold) ?? SKTypeface.Default;
        private static readonly SKColor LabelBackground = new(20, 20, 20, 200);
        private static readonly Dictionary<string, SKPath> Icons = PinIcons.All.ToDictionary(i => i.Id, i =>
        {
            SKPath path = SKPath.ParseSvgPathData(i.Path);
            path.FillType = SKPathFillType.EvenOdd;
            return path;
        });

        public MarkupImage(MarkupDocument document, string dimension)
        {
            _document = document;
            _dimension = dimension;
        }

        public bool HasAny => _document.PinsIn(_dimension).Any() || _document.ZonesIn(_dimension).Any();

        public void Draw(byte[] rgba, int left, int top, int width, int height)
        {
            MarkupFrame frame = MarkupFrame.Build(_document, new FixedMarkupView(_dimension, left, top, 1), width, height);
            if (frame.IsEmpty) return;

            // Skia draws into premultiplied pixels; the map's are almost all opaque or empty, so the round trip
            // changes nothing there
            using SKBitmap bitmap = new(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            Span<byte> pixels = bitmap.GetPixelSpan();
            Premultiply(rgba.AsSpan(0, width * height * 4), pixels);

            using (SKCanvas canvas = new(bitmap))
            {
                foreach (ZoneShape zone in frame.Zones) DrawZone(canvas, zone);
                foreach (ZoneShape zone in frame.Zones) DrawZoneLabel(canvas, zone);
                foreach (PinShape pin in frame.Pins) DrawPin(canvas, pin);
            }

            Unpremultiply(bitmap.GetPixelSpan(), rgba.AsSpan(0, width * height * 4));
        }

        private static void DrawZone(SKCanvas canvas, ZoneShape zone)
        {
            SKColor color = ColorOf(zone.Zone.Color);
            using SKPath path = new();
            path.AddPoly([.. zone.Points.Select(p => new SKPoint((float)p.X, (float)p.Y))], true);

            using SKPaint fill = new() { Color = color.WithAlpha(56), IsAntialias = true, Style = SKPaintStyle.Fill };
            using SKPaint edge = new() { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2, StrokeJoin = SKStrokeJoin.Round };
            canvas.DrawPath(path, fill);
            canvas.DrawPath(path, edge);
        }

        private static void DrawZoneLabel(SKCanvas canvas, ZoneShape zone)
        {
            if (zone.Zone.Label.Length == 0) return;

            using SKFont font = new(LabelFace, 13);
            float width = font.MeasureText(zone.Zone.Label);
            if (width + 8 > zone.ScreenWidth || font.Spacing + 4 > zone.ScreenHeight) return;

            DrawLabel(canvas, font, zone.Zone.Label, (float)zone.LabelAt.X - (width + 10) / 2, (float)zone.LabelAt.Y);
        }

        private static void DrawPin(SKCanvas canvas, PinShape pin)
        {
            float x = (float)pin.X, y = (float)pin.Y, radius = (float)MarkupEditor.PinRadius;

            using SKPaint disc = new() { Color = ColorOf(pin.Pin.Color), IsAntialias = true };
            using SKPaint edge = new() { Color = new SKColor(26, 26, 26), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
            canvas.DrawCircle(x, y, radius, disc);
            canvas.DrawCircle(x, y, radius, edge);

            using SKPaint glyph = new() { Color = MarkupColors.IsLight(pin.Pin.Color) ? new SKColor(32, 32, 32) : SKColors.White, IsAntialias = true };
            if (pin.Pin.Icon is string icon && Icons.TryGetValue(icon, out SKPath? path))
            {
                canvas.Save();
                canvas.Translate(x - 6, y - 6);
                canvas.Scale(0.75f);
                canvas.DrawPath(path, glyph);
                canvas.Restore();
            }
            else
            {
                canvas.DrawCircle(x, y, 2.5f, glyph);
            }

            if (pin.Pin.Label.Length > 0)
            {
                using SKFont font = new(LabelFace, 12);
                DrawLabel(canvas, font, pin.Pin.Label, x + radius + 4, y);
            }
        }

        // A line of white text on a dark rounded backing, its left edge at left and centred on y
        private static void DrawLabel(SKCanvas canvas, SKFont font, string text, float left, float y)
        {
            float width = font.MeasureText(text) + 10, height = font.Spacing + 4;
            SKRect backing = SKRect.Create(MathF.Round(left), MathF.Round(y - height / 2), MathF.Ceiling(width), MathF.Ceiling(height));

            using SKPaint back = new() { Color = LabelBackground, IsAntialias = true };
            canvas.DrawRoundRect(backing, 3, 3, back);

            using SKPaint ink = new() { Color = SKColors.White, IsAntialias = true };
            float baseline = backing.Top + 2 - font.Metrics.Ascent;
            canvas.DrawText(text, backing.Left + 5, baseline, SKTextAlign.Left, font, ink);
        }

        private static SKColor ColorOf(string hex)
        {
            var (r, g, b) = MarkupColors.Parse(hex);
            return new SKColor(r, g, b);
        }

        private static void Premultiply(ReadOnlySpan<byte> from, Span<byte> to)
        {
            for (int i = 0; i < from.Length; i += 4)
            {
                int a = from[i + 3];
                to[i] = (byte)((from[i] * a + 127) / 255);
                to[i + 1] = (byte)((from[i + 1] * a + 127) / 255);
                to[i + 2] = (byte)((from[i + 2] * a + 127) / 255);
                to[i + 3] = (byte)a;
            }
        }

        private static void Unpremultiply(ReadOnlySpan<byte> from, Span<byte> to)
        {
            for (int i = 0; i < from.Length; i += 4)
            {
                int a = from[i + 3];
                if (a == 0)
                {
                    to[i] = to[i + 1] = to[i + 2] = to[i + 3] = 0;
                    continue;
                }
                to[i] = (byte)Math.Min(255, (from[i] * 255 + a / 2) / a);
                to[i + 1] = (byte)Math.Min(255, (from[i + 1] * 255 + a / 2) / a);
                to[i + 2] = (byte)Math.Min(255, (from[i + 2] * 255 + a / 2) / a);
                to[i + 3] = (byte)a;
            }
        }
    }
}
