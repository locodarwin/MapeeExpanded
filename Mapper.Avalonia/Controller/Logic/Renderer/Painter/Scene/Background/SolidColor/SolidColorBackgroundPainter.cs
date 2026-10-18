using Avalonia.Media;

namespace Mapper.Gui.Logic
{
    public class SolidColorBackgroundPainter : IPainter<BackgroundPaintArgs>
    {
        public void Paint(DrawingContext drawingContext, BackgroundPaintArgs args)
        {
            drawingContext.DrawRectangle(new SolidColorBrush(args.Background.SolidColor), null, args.BackgroundArea);
        }
    }
}
