using StbImageSharp;
using System.Drawing;

namespace Mapper
{
    public sealed class ReadOnlyBitmap : IReadOnlyBitmap
    {
        // Bgra32, not premultiplied
        public byte[] PixelData { get; }

        public Size Size { get; }

        public ReadOnlyBitmap(string path)
        {
            using Stream stream = File.OpenRead(path);
            PixelData = Decode(stream, out Size size);
            Size = size;
        }
        public ReadOnlyBitmap(Stream stream)
        {
            PixelData = Decode(stream, out Size size);
            Size = size;
        }

        private static byte[] Decode(Stream stream, out Size size)
        {
            ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            size = new Size(image.Width, image.Height);

            byte[] data = image.Data;
            for (int i = 0; i < data.Length; i += 4)
            {
                (data[i], data[i + 2]) = (data[i + 2], data[i]);
            }

            return data;
        }

        public Color GetPixel(int x, int y)
        {
            int offset = GetOffset(x, y);
            return Color.FromArgb(PixelData[offset + 3], PixelData[offset + 2], PixelData[offset + 1], PixelData[offset]);
        }
        public Color GetAverageColor()
        {
            return GetAverageColor(0, 0, Size.Width, Size.Height);
        }
        public Color GetAverageColor(int x1, int y1, int width, int height)
        {
            int r = 0, g = 0, b = 0, a = 0;
            float opaqueCount = 0;

            for (int y = y1; y < y1 + height; y++)
            {
                for (int x = x1; x < x1 + width; x++)
                {
                    Color color = GetPixel(x, y);

                    if (color.A > 0)
                    {
                        r += color.R;
                        g += color.G;
                        b += color.B;
                        opaqueCount++;
                    }

                    a += color.A;
                }
            }

            float count = width * height;
            return Color.FromArgb((byte)(a / count), (byte)(r / opaqueCount), (byte)(g / opaqueCount), (byte)(b / opaqueCount));
        }

        private int GetOffset(int x, int y)
        {
            return (y * Size.Width + x) * 4;
        }
    }
}
