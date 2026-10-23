using Mapper.Gui.Model;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace Mapper.Gui
{
    // Draws a ToolIcon at a size, in a brush the caller can recolour (the WPF GUI has its own copy)
    public static class ToolIconView
    {
        private static readonly Dictionary<string, Geometry> _geometries = [];

        public static Control Create(ToolIcon icon, IBrush ink, double size)
        {
            Avalonia.Controls.Canvas canvas = new() { Width = ToolIcon.Box, Height = ToolIcon.Box };
            canvas.Children.Add(new Path()
            {
                Data = Parse(icon.Stroke),
                Stroke = ink,
                StrokeThickness = ToolIcon.Thickness,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round
            });
            if (icon.Fill is string fill) canvas.Children.Add(new Path() { Data = Parse(fill), Fill = ink });

            return new Viewbox() { Width = size, Height = size, Child = canvas };
        }

        private static Geometry Parse(string data)
        {
            if (!_geometries.TryGetValue(data, out Geometry? geometry)) _geometries[data] = geometry = Geometry.Parse(data);
            return geometry;
        }
    }
}
