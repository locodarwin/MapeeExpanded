using Avalonia.Media;

namespace Mapper.Gui.Logic
{
    public interface IPainter
    {
        void Paint(DrawingContext drawingContext);
    }
    public interface IPainter<TArgs>
    {
        void Paint(DrawingContext drawingContext, TArgs args);
    }
}
