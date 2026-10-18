using WorldEditor;

namespace Mapper
{
    public interface ICanvas : IDisposable
    {
        Coords TopLeftPoint { get; }

        void SetPixel(int xInWorld, int zInWorld, VecRgb color);
        int PixelWidth { get; }
        int PixelHeight { get; }

        // Bgra32 rows of PixelWidth pixels, or null while nothing has been drawn
        byte[]? GetPixels();
    }
}
