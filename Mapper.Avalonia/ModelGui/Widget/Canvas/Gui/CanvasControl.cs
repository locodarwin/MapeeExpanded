using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Mapper.Gui.Logic;
using System.Collections.Generic;

namespace Mapper.Gui
{
    // The map surface. Painters draw straight into it, in the order they were added, whenever it renders
    public class CanvasControl : Control
    {
        public IList<IPainter> Painters { get; } = new List<IPainter>();

        // Plain fields so background threads can read them; Avalonia properties may only be read on the UI thread
        public double ActualWidth { get; private set; }
        public double ActualHeight { get; private set; }
        public Point MousePosition { get; private set; }
        public bool IsMouseOver { get; private set; }

        public CanvasControl()
        {
            ClipToBounds = true;
            Focusable = true;
        }

        public void AddPainter(IPainter painter)
        {
            Painters.Add(painter);
        }

        // Pixels per layout unit on the screen the canvas is on
        public double RenderScaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            ActualWidth = e.NewSize.Width;
            ActualHeight = e.NewSize.Height;

            base.OnSizeChanged(e);
        }

        protected override void OnPointerEntered(PointerEventArgs e)
        {
            IsMouseOver = true;
            MousePosition = e.GetPosition(this);
            base.OnPointerEntered(e);
        }
        protected override void OnPointerExited(PointerEventArgs e)
        {
            IsMouseOver = false;
            base.OnPointerExited(e);
        }
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            MousePosition = e.GetPosition(this);
            base.OnPointerMoved(e);
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            Focus();
            base.OnPointerPressed(e);
        }

        public override void Render(DrawingContext context)
        {
            Rect bounds = new(Bounds.Size);

            // Transparent, but it makes the whole surface take pointer input
            context.FillRectangle(Brushes.Transparent, bounds);

            using (context.PushClip(bounds))
            {
                foreach (IPainter painter in Painters)
                {
                    painter.Paint(context);
                }
            }
        }
    }
}
