using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Mapper.Gui.Model;
using System.Runtime.InteropServices;

namespace Mapper.Gui.Logic
{
    public class CheckerImageGenerator : ICheckerImageGenerator
    {
        private byte[]? _rawPixels;

        public Bitmap Generate(PixelSize size, int checkerSize, ColorPair pair)
        {
            int length = size.Width * size.Height * 4;
            if (_rawPixels is null || _rawPixels.Length < length) 
            {
                _rawPixels = new byte[length];
            }

            for (int y = 0; y < size.Height; y++)
            {
                for (int x = 0; x < size.Width; x++)
                {
                    bool isEven = x / checkerSize % 2 == 0 ^ y / checkerSize % 2 == 0;
                    Color color = isEven ? pair.Even : pair.Odd;

                    int offset = (y * size.Width + x) * 4;
                    _rawPixels[offset] = color.B;
                    _rawPixels[offset + 1] = color.G;
                    _rawPixels[offset + 2] = color.R;
                    _rawPixels[offset + 3] = 255;
                }
            }

            WriteableBitmap bitmap = new(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (ILockedFramebuffer buffer = bitmap.Lock())
            {
                for (int y = 0; y < size.Height; y++)
                {
                    Marshal.Copy(_rawPixels, y * size.Width * 4, buffer.Address + y * buffer.RowBytes, size.Width * 4);
                }
            }

            return bitmap;
        }
    }
}
