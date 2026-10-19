using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;

namespace Mapper.Gui
{
    public static class CanvasBitmap
    {
        // Copies a rendered canvas into a bitmap, or null if nothing was drawn on it
        public static Bitmap? Create(ICanvas canvas)
        {
            byte[]? pixels = canvas.GetPixels();
            if (pixels is null) return null;

            WriteableBitmap output = new(new PixelSize(canvas.PixelWidth, canvas.PixelHeight), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using (ILockedFramebuffer buffer = output.Lock())
            {
                int rowLength = canvas.PixelWidth * 4;
                for (int y = 0; y < canvas.PixelHeight; y++)
                {
                    Marshal.Copy(pixels, y * rowLength, buffer.Address + y * buffer.RowBytes, rowLength);
                }
            }

            return output;
        }
    }
}
