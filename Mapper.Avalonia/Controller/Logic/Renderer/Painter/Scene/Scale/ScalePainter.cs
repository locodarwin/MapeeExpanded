using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;

namespace Mapper.Gui.Logic
{
    // Moves and zooms what is drawn inside it. Unlike the WPF painter, which pushes onto a drawing group of its own,
    // this one hands back the pushed state for the caller to pop once the scene is drawn
    public class ScalePainter
    {
        public IDisposable Push(DrawingContext drawingContext, ScalePaintArgs args)
        {
            if (!args.Enabled) return new States();

            Matrix transform = Matrix.CreateScale(args.ZoomCoefficient, args.ZoomCoefficient) * Matrix.CreateTranslation(args.Offset.X, args.Offset.Y);

            return new States(
                drawingContext.PushTransform(transform),
                drawingContext.PushRenderOptions(new RenderOptions()
                {
                    BitmapInterpolationMode = CalibrateScaling(args.ZoomLevel, args.LevelIncrement, args.RenderScaling),
                    EdgeMode = EdgeMode.Aliased
                }));
        }
        private static BitmapInterpolationMode CalibrateScaling(int level, double levelIncrement, double renderScaling)
        {
            BitmapInterpolationMode mode;

            int center = -(int)Math.Ceiling(Math.Log(renderScaling, levelIncrement));

            if (level >= center) mode = BitmapInterpolationMode.None;
            else if (level >= center - 5) mode = BitmapInterpolationMode.LowQuality;
            else mode = BitmapInterpolationMode.HighQuality;

            return mode;
        }

        private sealed class States : IDisposable
        {
            private readonly List<DrawingContext.PushedState> _states;

            public States(params DrawingContext.PushedState[] states)
            {
                _states = new List<DrawingContext.PushedState>(states);
            }

            public void Dispose()
            {
                for (int i = _states.Count - 1; i >= 0; i--) _states[i].Dispose();
                _states.Clear();
            }
        }
    }
}
