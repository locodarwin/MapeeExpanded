using Avalonia.Controls.Primitives;
using Mapper.Gui.Model;

namespace Mapper.Gui
{
    public static class ScrollbarUtilities
    {
        public static void AdjustScrollbar(ScrollBar scrollbarControl, IScrollbarWidget scrollbar, double trackLength) 
        {
            if (scrollbar.LoadedArea is null || scrollbar.VisibleArea is null ||
                trackLength <= 0 || scrollbar.LoadedArea.Value.IsEmpty())
            {
                scrollbarControl.IsVisible = false;
                return;
            }

            scrollbarControl.Minimum = scrollbar.LoadedArea.Value.Point1;
            scrollbarControl.Maximum = scrollbar.LoadedArea.Value.Point2 - scrollbar.VisibleArea.Value.Size + 1;

            double portSize = FindViewportSize(scrollbar.VisibleArea.Value.Size / scrollbar.LoadedArea.Value.Size * trackLength, trackLength, scrollbarControl.Maximum, scrollbarControl.Minimum);
            if (scrollbar.LoadedArea.Value.Size > 0 && !double.IsNaN(portSize) && portSize > 0)
            {
                scrollbarControl.ViewportSize = portSize;
                scrollbarControl.Value = scrollbar.VisibleArea.Value.Point1;
                scrollbarControl.IsVisible = true;
            }
            else
            {
                scrollbarControl.IsVisible = false;
            }

            static double FindViewportSize(double thumbSize, double trackLength, double max, double min)
            {
                return (-thumbSize * max + thumbSize * min) / (thumbSize - trackLength);
            }
        }
    }
}
