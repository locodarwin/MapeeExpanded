using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mapper.Gui
{
    public static class CanvasBitmap
    {
        // Copies a rendered canvas into a frozen bitmap, or null if nothing was drawn on it
        public static ImageSource? Create(ICanvas canvas)
        {
            byte[]? pixels = canvas.GetPixels();
            if (pixels is null) return null;

            WriteableBitmap output = new(canvas.PixelWidth, canvas.PixelHeight, 96, 96, PixelFormats.Bgra32, null);
            output.WritePixels(new Int32Rect(0, 0, canvas.PixelWidth, canvas.PixelHeight), pixels, canvas.PixelWidth * 4, 0);
            output.Freeze();

            return output;
        }
    }
}
