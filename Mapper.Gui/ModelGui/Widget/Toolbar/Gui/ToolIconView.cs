using Mapper.Gui.Model;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Mapper.Gui
{
    // Draws a ToolIcon at a size, in a brush the caller can recolour (the Avalonia GUI has its own copy)
    public static class ToolIconView
    {
        private static readonly Dictionary<string, Geometry> _geometries = [];

        public static FrameworkElement Create(ToolIcon icon, Brush ink, double size)
        {
            System.Windows.Controls.Canvas canvas = new() { Width = ToolIcon.Box, Height = ToolIcon.Box };
            canvas.Children.Add(new Path()
            {
                Data = Parse(icon.Stroke),
                Stroke = ink,
                StrokeThickness = ToolIcon.Thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            });
            if (icon.Fill is string fill) canvas.Children.Add(new Path() { Data = Parse(fill), Fill = ink });

            return new Viewbox() { Width = size, Height = size, Child = canvas };
        }

        private static Geometry Parse(string data)
        {
            if (!_geometries.TryGetValue(data, out Geometry? geometry))
            {
                geometry = Geometry.Parse(data);
                geometry.Freeze();
                _geometries[data] = geometry;
            }
            return geometry;
        }
    }
}
